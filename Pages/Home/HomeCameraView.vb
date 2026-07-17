Imports System.IO
Imports System.Text
Imports System.Threading
Imports System.Windows
Imports System.Windows.Threading
Imports MetroSystemForDotNet
Imports Microsoft.Web.WebView2.Core
Imports Microsoft.Win32
Imports OpenCvSharp
Imports OpenCvSharp.WpfExtensions
Imports Cv = OpenCvSharp

Partial Class HomePage

    Private Sub OnFrameArrived(deviceId As String, img As BitmapSource)

        If RenderImage Is Nothing Then Return

        ' 根據目前流程階段，計算當下應該接收哪一台相機的畫面
        Dim currentActiveId = If(_flowStage = DetectionFlowStage.Barcode OrElse _flowStage = DetectionFlowStage.Ocr, _ocrCameraId, _matchCameraId)

        ' 如果送來畫面的硬體設備不是目前階段需要的相機，直接攔截，防止畫面錯亂
        If Not String.Equals(deviceId, currentActiveId, StringComparison.OrdinalIgnoreCase) Then Return

        RenderImage.Dispatcher.BeginInvoke(Sub()

                                               If RenderImage Is Nothing Then Return

                                               ' 如果是匹配階段，由 UpdateFrame 負責高頻渲染帶框畫面，避免此處原生畫面覆蓋造成閃爍
                                               If _flowStage <> DetectionFlowStage.Matching Then
                                                   RenderImage.Source = img
                                                   If _activePreviewWin IsNot Nothing Then
                                                       _activePreviewWin.UpdateFrame(img)
                                                   End If
                                               End If

                                               ' ⭐ 保存最後一幀（UI層）
                                               _lastFrameBitmap = img

                                           End Sub)
    End Sub
    Private Sub BtnStart_Click(sender As Object, e As RoutedEventArgs)

        Try

            If _isStreaming Then Return

            ' 從 ComboBox 獲取選定的相機
            Dim selectedCam = TryCast(CameraComboBox.SelectedItem, CameraInfo)
            If selectedCam IsNot Nothing Then
                ' 如果手動點選，依目前階段更新對應的相機暫存
                If _flowStage = DetectionFlowStage.Barcode OrElse _flowStage = DetectionFlowStage.Ocr Then
                    _ocrCameraId = selectedCam.DeviceId
                Else
                    _matchCameraId = selectedCam.DeviceId
                End If
                Logger.Info($"手動選定相機: {selectedCam.DisplayName}")
            ElseIf String.IsNullOrWhiteSpace(_matchCameraId) Then
                Logger.Error("未選擇相機設備")
                MessageBox.Show("請先選擇相機設備")
                Return
            End If

            AddHandler CameraService.Instance.FrameArrived, AddressOf OnFrameArrived

            ' 既然開啟串流，就把兩台工作相機都安全啟動（如果是同一台硬體，Service 內部會自動忽略重複啟動）
            If Not String.IsNullOrWhiteSpace(_matchCameraId) Then
                CameraService.Instance.StartCamera(_matchCameraId)
            End If

            If Not String.IsNullOrWhiteSpace(_ocrCameraId) AndAlso _ocrCameraId <> _matchCameraId Then
                CameraService.Instance.StartCamera(_ocrCameraId)
            End If

            _isStreaming = True
            Logger.Info("相機已啟動（雙相機通道就緒）")

        Catch ex As Exception
            MessageBox.Show(ex.Message)
        End Try

    End Sub

    ' =========================================
    ' ComboBox 選擇變更事件
    ' =========================================
    Private Sub CameraComboBox_SelectionChanged(sender As Object, e As SelectionChangedEventArgs)
        ' 【防禦關鍵】如果是程式自動刷新（例如 RefreshCameraComboBox）觸發的選擇變更，直接攔截！
        ' 只有當使用者「真正打開下拉選單」或「點擊聚焦」時，才認定是手動修改，防止事件無限遞迴死鎖
        If Not CameraComboBox.IsDropDownOpen AndAlso Not CameraComboBox.IsFocused Then Return

        Try
            Dim cam = TryCast(CameraComboBox.SelectedItem, CameraInfo)
            If cam IsNot Nothing Then

                Dim savedIds = My.Settings.CameraDeviceIds
                If savedIds Is Nothing Then savedIds = New System.Collections.Specialized.StringCollection()

                ' 確保 StringCollection 陣列長度至少有 2，防止索引越界
                While savedIds.Count < 2
                    savedIds.Add(cam.DeviceId)
                End While

                ' 根據目前首頁正處於什麼階段，決定這一次的手動切換是改「相機 1」還是「相機 2」
                If _flowStage = DetectionFlowStage.Barcode OrElse _flowStage = DetectionFlowStage.Ocr Then
                    _ocrCameraId = cam.DeviceId
                    savedIds(1) = cam.DeviceId
                    Logger.Info($"[UI手動變更] 已更新相機 2 (OCR/條碼) 為: {cam.DisplayName}")
                Else
                    _matchCameraId = cam.DeviceId
                    savedIds(0) = cam.DeviceId
                    My.Settings.CameraDeviceId = cam.DeviceId ' 保持與舊的單一欄位相容
                    Logger.Info($"[UI手動變更] 已更新相機 1 (定位匹配) 為: {cam.DisplayName}")
                End If

                ' 儲存陣列，並通知全域變更（這會觸發相機服務重啟硬體）
                My.Settings.CameraDeviceIds = savedIds
                My.Settings.Save()

                CameraManager.NotifyCameraChanged()
            End If
        Catch ex As Exception
            Logger.Error($"相機選擇變更失敗: {ex.Message}")
        End Try
    End Sub

    Private Sub BtnStop_Click(sender As Object, e As RoutedEventArgs)

        Try

            If Not _isStreaming Then Return

            RemoveHandler CameraService.Instance.FrameArrived, AddressOf OnFrameArrived

            CameraService.Instance.StopAll()

            _isStreaming = False

            Logger.Info("相機已停止（畫面已凍結）")

        Catch ex As Exception
            MessageBox.Show(ex.Message)
        End Try

    End Sub
    Private Sub BtnSave_Click(sender As Object, e As RoutedEventArgs)

        Try
            If _lastFrameBitmap Is Nothing Then
                MessageBox.Show("沒有可保存的畫面")
                Return
            End If

            Dim dlg As New SaveFileDialog With {
            .Filter = "PNG Image|*.png|JPG Image|*.jpg",
            .FileName = $"capture_{DateTime.Now:yyyyMMdd_HHmmss}.png"
        }

            If dlg.ShowDialog() <> True Then Return

            Dim encoder As BitmapEncoder

            Dim ext = Path.GetExtension(dlg.FileName).ToLower()

            If ext = ".jpg" OrElse ext = ".jpeg" Then
                encoder = New JpegBitmapEncoder()
            Else
                encoder = New PngBitmapEncoder()
            End If

            encoder.Frames.Add(BitmapFrame.Create(_lastFrameBitmap))

            Using fs As New FileStream(dlg.FileName, FileMode.Create)
                encoder.Save(fs)
            End Using

            Logger.Info($"畫面已保存: {dlg.FileName}")

        Catch ex As Exception
            MessageBox.Show(ex.Message)
        End Try

    End Sub
End Class
