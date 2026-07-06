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
        If Not String.Equals(deviceId, _detectCameraId, StringComparison.OrdinalIgnoreCase) Then Return

        RenderImage.Dispatcher.BeginInvoke(Sub()

                                               If RenderImage Is Nothing Then Return

                                               RenderImage.Source = img

                                               ' ⭐ 保存最後一幀（UI層）
                                               _lastFrameBitmap = img

                                           End Sub)
    End Sub
    Private Sub BtnStart_Click(sender As Object, e As RoutedEventArgs)

        Try

            If _isStreaming Then Return

            ' 從 ComboBox 獲取選定的相機
            If CameraComboBox.SelectedValue IsNot Nothing Then
                _detectCameraId = CameraComboBox.SelectedValue.ToString()
                Logger.Info($"選定相機: {_detectCameraId}")
            ElseIf String.IsNullOrWhiteSpace(_detectCameraId) Then
                Logger.Error("未選擇相機設備")
                MessageBox.Show("請先選擇相機設備")
                Return
            End If

            AddHandler CameraService.Instance.FrameArrived, AddressOf OnFrameArrived

            If Not String.IsNullOrWhiteSpace(_detectCameraId) Then
                CameraService.Instance.StartCamera(_detectCameraId)
            End If

            _isStreaming = True

            Logger.Info("相機已啟動")

        Catch ex As Exception
            MessageBox.Show(ex.Message)
        End Try

    End Sub

    ' =========================================
    ' ComboBox 選擇變更事件
    ' =========================================
    Private Sub CameraComboBox_SelectionChanged(sender As Object, e As SelectionChangedEventArgs)
        Try
            If CameraComboBox.SelectedValue IsNot Nothing Then
                _detectCameraId = CameraComboBox.SelectedValue.ToString()
                Logger.Info($"相機選擇已改變: {_detectCameraId}")
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
