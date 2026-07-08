Imports OpenCvSharp
Imports OpenCvSharp.WpfExtensions
Imports System.Windows
Imports System.Windows.Input

Imports CvRect = OpenCvSharp.Rect

Imports CvPoint = OpenCvSharp.Point
Imports WpfPoint = System.Windows.Point

Public Class AlgorithmPage

    Public Sub New()
        InitializeComponent()

        AddHandler Me.Loaded, AddressOf AlgorithmPage_Loaded

        AddHandler LanguageManager.LanguageChanged, AddressOf RefreshLanguageUI


    End Sub

    ' =========================
    ' Mat
    ' =========================
    Private _srcMat As Mat
    Private _templateMat As Mat
    Private _matchMat As Mat
    Private _autoParamsApplied As Boolean = False

    ' =========================
    ' ROI
    ' =========================
    Private _roiCtrl As RoiController
    Private _roi As CvRect

    ' 視窗
    Private templateZoom As Double = 1.0
    Private templateIsPanning As Boolean = False
    Private templateLastPanPoint As WpfPoint

    ' 當前選擇的相機（多相機建模使用） - 優先使用設定中儲存的相機
    Private _selectedCameraId As String = If(String.IsNullOrWhiteSpace(My.Settings.CameraDeviceId), GetCamId(0), My.Settings.CameraDeviceId)
    Private _selectedCameraSlot As Integer = 0

    ' 當前建模任務的父資料夾名稱（第一個相機建模時輸入，第二個沿用）
    Private _currentTemplateName As String = ""
    Private _templateCameraId As String = ""

    ' 自動參數只在每次載入新原圖後的第一次生成時套用，之後不複寫使用者手動調整的值
    Private _autoParamsAppliedOnce As Boolean = False

    ' =========================
    ' Loaded
    ' =========================
    Private Async Sub AlgorithmPage_Loaded(sender As Object, e As RoutedEventArgs)

        RefreshLanguageUI()

        Await Task.Run(Sub()

                       End Sub)

        ' ⭐ 模板 restore 也背景處理
        Dim lastTemplatePath = LastTemplateStore.Load()

        If Not String.IsNullOrWhiteSpace(lastTemplatePath) Then

            Dim data = Await Task.Run(Function()
                                          Return TemplateManager.LoadTemplate(lastTemplatePath)
                                      End Function)

            If data IsNot Nothing Then

                Dispatcher.Invoke(Sub()
                                      ApplyTemplate(data.Template, data.Config)
                                      TemplateStatusText.Text = "已回復"
                                  End Sub)

            End If

        End If


    End Sub
#Region "獲取圖像"
    Private Async Sub GetSource_Click(sender As Object, e As RoutedEventArgs)
        Try
            ' 彈出相機選擇對話窗
            Dim picker As New CameraPickDialog()
            picker.Owner = System.Windows.Window.GetWindow(Me) ' 確保 Owner 正確

            If picker.ShowDialog() <> True Then Return

            _selectedCameraId = picker.SelectedCameraId
            _selectedCameraSlot = picker.SelectedCameraSlot

            ' 使用 Dispatcher 稍遲執行後續邏輯，避免與剛關閉的對話窗 Handle 衝突
            Application.Current.Dispatcher.BeginInvoke(Sub()
                                                           Try
                                                               If String.IsNullOrWhiteSpace(_selectedCameraId) Then
                                                                   ErrorDialogHelper.ShowError("尚未設定相機")
                                                                   Return
                                                               End If

                                                               ' --- 後續啟動相機與等待第一幀的邏輯移到這裡 ---
                                                               CameraService.Instance.StartCamera(_selectedCameraId)
                                                               ' ... (其餘程式碼保持不變) ...

                                                           Catch ex As Exception
                                                               ExceptionHelper.ShowError(ex)
                                                           End Try
                                                       End Sub)

            ' 啟動相機（若已在執行中則無效，不會重複啟動）
            CameraService.Instance.StartCamera(_selectedCameraId)

            ' 先檢查是否已有快取幀（相機已在執行中時可立即取得）
            Dim frame = CameraService.Instance.GetFrame(_selectedCameraId)

            ' 若無快取幀，訂閱 FrameArrived 等待第一幀（最長 8 秒）
            ' 比 Thread.Sleep 輪詢更可靠：DSHOW 初始化通常需要 3～8 秒
            If frame Is Nothing Then
                Dim tcs As New TaskCompletionSource(Of BitmapSource)()
                Dim targetId = _selectedCameraId

                Dim handler As Action(Of String, BitmapSource) = Nothing
                handler = Sub(id As String, img As BitmapSource)
                              If String.Equals(id, targetId, StringComparison.OrdinalIgnoreCase) Then
                                  RemoveHandler CameraService.Instance.FrameArrived, handler
                                  tcs.TrySetResult(img)
                              End If
                          End Sub

                AddHandler CameraService.Instance.FrameArrived, handler

                ' 訂閱後再次確認（避免幀在 GetFrame 和 AddHandler 之間到達的競爭條件）
                Dim recheck = CameraService.Instance.GetFrame(_selectedCameraId)
                If recheck IsNot Nothing Then
                    RemoveHandler CameraService.Instance.FrameArrived, handler
                    tcs.TrySetResult(recheck)
                End If

                Dim completed = Await Task.WhenAny(tcs.Task, Task.Delay(8000))
                RemoveHandler CameraService.Instance.FrameArrived, handler

                If completed Is tcs.Task Then
                    frame = tcs.Task.Result
                End If
            End If

            If frame Is Nothing Then
                ErrorDialogHelper.ShowError($"尚未取得相機 {_selectedCameraSlot + 1} 的影像（相機啟動逾時，請確認相機已連接）")
                Return
            End If

            _srcMat?.Dispose()
            _srcMat = BitmapSourceToMat(frame)

            SrcImage.Source = ImageConvertHelper.ToBitmap(_srcMat)

            _roiCtrl = New RoiController(
                RoiCanvas,
                SrcImage,
                _srcMat)

            SrcImage.Source = ImageConvertHelper.ToBitmap(_srcMat)
            SrcImage.UpdateLayout()

            _autoParamsAppliedOnce = False  ' 新圖：下次生成才套用自動參數
            ResetUI()

        Catch ex As Exception
            ExceptionHelper.ShowError(ex)
        End Try
    End Sub
#End Region
    ' =========================
    ' Load Image
    ' =========================
    Private Sub LoadSource_Click(sender As Object, e As RoutedEventArgs)

        SafeRun(Sub()

                    Dim path = DialogHelper.OpenImage()
                    If String.IsNullOrWhiteSpace(path) Then Return

                    _srcMat = ImageFileService.Load(path)
                    SrcImage.Source = ImageConvertHelper.ToBitmap(_srcMat)

                    _roiCtrl = New RoiController(RoiCanvas, SrcImage, _srcMat)

                    _autoParamsAppliedOnce = False  ' 新圖：下次生成才套用自動參數
                    ResetUI()

                End Sub)

    End Sub
    ' =========================
    ' 縮放
    ' =========================
    Private zoom As Double = 1.0
    Private pan As WpfPoint = New WpfPoint(0, 0)

    Private isPanning As Boolean = False
    Private lastPanPoint As WpfPoint
    Private Sub Viewer_MouseWheel(sender As Object, e As MouseWheelEventArgs)

        Dim scale As ScaleTransform = Nothing

        If sender Is ViewerBorder Then
            scale = ImageScale

        ElseIf sender Is TemplateBorder Then
            scale = TemplateScale

        ElseIf sender Is ResultBorder Then
            scale = ResultScale
        End If

        If scale Is Nothing Then Return

        Dim zoomFactor As Double =
        If(e.Delta > 0, 1.1, 0.9)

        scale.ScaleX *= zoomFactor
        scale.ScaleY *= zoomFactor

    End Sub

    Private Sub Viewer_MouseDown(sender As Object, e As MouseButtonEventArgs)

        If e.MiddleButton <> MouseButtonState.Pressed Then Return

        isPanning = True

        currentBorder = CType(sender, Border)

        If sender Is ViewerBorder Then
            currentTranslate = ImageTranslate
        ElseIf sender Is TemplateBorder Then
            currentTranslate = TemplateTranslate
        ElseIf sender Is ResultBorder Then
            currentTranslate = ResultTranslate
        End If

        lastPanPoint = e.GetPosition(currentBorder)

    End Sub

    Private currentTranslate As TranslateTransform
    Private currentBorder As Border
    Private Sub Viewer_MouseMove(sender As Object, e As MouseEventArgs)

        If Not isPanning Then Return

        Dim pos = e.GetPosition(currentBorder)

        Dim dx = pos.X - lastPanPoint.X
        Dim dy = pos.Y - lastPanPoint.Y

        currentTranslate.X += dx
        currentTranslate.Y += dy

        lastPanPoint = pos

    End Sub

    Private Sub Viewer_MouseUp(sender As Object, e As MouseButtonEventArgs)

        isPanning = False

    End Sub
    '觸控調優
    Private Sub ImageHost_ManipulationDelta(sender As Object, e As ManipulationDeltaEventArgs)

        zoom *= e.DeltaManipulation.Scale.X

        zoom = Math.Max(0.2, Math.Min(5.0, zoom))

        ImageScale.ScaleX = zoom
        ImageScale.ScaleY = zoom

    End Sub

    ' =========================
    ' ROI events
    ' =========================
    Private Sub RoiCanvas_MouseLeftButtonDown(sender As Object, e As MouseButtonEventArgs)
        _roiCtrl?.MouseDown(e)
    End Sub

    Private Sub RoiCanvas_MouseMove(sender As Object, e As MouseEventArgs)
        _roiCtrl?.MouseMove(e)
    End Sub

    Private Sub RoiCanvas_MouseLeftButtonUp(sender As Object, e As MouseButtonEventArgs)

        Try
            _roiCtrl.MouseUp()

            _roi = _roiCtrl.Roi

            If _roi.Width <= 0 OrElse _roi.Height <= 0 Then
                RoiStatusText.Text = "ROI 無效"
                Return
            End If

            RoiStatusText.Text = "已選擇"

        Catch ex As Exception
            Logger.Error("ROI錯誤：" & ex.ToString())
            ErrorDialogHelper.ShowError("ROI錯誤: " & ex.Message)
        End Try

    End Sub

    ' =========================
    ' Create Template
    ' =========================
    Private Async Sub CreateTemplate_Click(sender As Object, e As RoutedEventArgs)

        Try
            If _srcMat Is Nothing Then
                MessageBox.Show("No image")
                Return
            End If

            If _roi.Width <= 0 OrElse _roi.Height <= 0 Then
                MessageBox.Show("ROI empty")
                Return
            End If

            TemplateStatusText.Text = "生成中..."

            Dim safeRoi = New CvRect(_roi.X, _roi.Y, _roi.Width, _roi.Height)
            Dim srcCopy = _srcMat

            ' 在 UI 執行緒先讀取滑塊值（Task.Run 不可跨執行緒存取 UI）
            Dim manualCannyLow = CInt(CannyLowSlider.Value)
            Dim manualCannyHigh = CInt(CannyHighSlider.Value)
            Dim manualMinArea = CInt(MinAreaSlider.Value)
            Dim alreadyHasAutoParams = _autoParamsApplied   ' 第一次生成後為 True

            Dim result = Await Task.Run(Function()
                                            ' 在背景執行緒中計算自動參數（基於圖像統計）
                                            Dim autoP = ComputeAutoParams(srcCopy, safeRoi)

                                            ' 第一次生成時使用自動計算值；之後採用使用者在 UI 手動調整的滑塊值
                                            Dim opts As New TemplateMatchOptions With {
                                                .CannyLow = If(alreadyHasAutoParams, manualCannyLow, autoP.CannyLow),
                                                .CannyHigh = If(alreadyHasAutoParams, manualCannyHigh, autoP.CannyHigh),
                                                .MinContourArea = If(alreadyHasAutoParams, manualMinArea, autoP.MinArea)
                                            }

                                            Dim preview As Mat = Nothing
                                            Dim mat = TemplateMatcher.CreateTemplate(srcCopy, safeRoi, opts, preview)
                                            Return (mat, preview, autoP)
                                        End Function)

            ' Dispose old template mat before replacing
            Dim oldTpl = _templateMat
            _templateMat = result.Item1
            oldTpl?.Dispose()

            _templateCameraId = _selectedCameraId

            TemplateImage.Source = ImageConvertHelper.ToBitmap(result.Item2)

            ' 只在第一次生成時將自動計算參數套用到 UI 滑塊；
            ' 之後保留使用者手動調整的值，不覆寫
            If Not _autoParamsApplied Then
                ApplyAutoParams(result.Item3)
                _autoParamsApplied = True
            End If

            Dim ap = result.Item3
            TemplateStatusText.Text = $"✓ 金字塔={ap.Pyramid}  Canny={ap.CannyLow}/{ap.CannyHigh}  MinArea={ap.MinArea}"
        Catch ex As Exception
            MessageBox.Show(ex.ToString())
        End Try

    End Sub

    ' =========================
    ' Match image
    ' =========================
    Private Sub LoadMatch_Click(sender As Object, e As RoutedEventArgs)

        SafeRun(Sub()

                    If _templateMat Is Nothing Then Return

                    Dim path = DialogHelper.OpenImage()
                    If String.IsNullOrWhiteSpace(path) Then Return

                    _matchMat = ImageFileService.Load(path)

                    RunTemplateMatch()

                End Sub)

    End Sub

    Private Sub SaveTemplate_Click(sender As Object, e As RoutedEventArgs)

        SafeRun(Sub()

                    If _templateMat Is Nothing Then Return

                    ' 第一次儲存才詢問父資料夾名稱
                    If String.IsNullOrWhiteSpace(_currentTemplateName) Then
                        Dim name = Microsoft.VisualBasic.InputBox(
                            "請輸入模板組名稱（兩個相機共用此名稱）",
                            "建立模板組",
                            $"Template_{DateTime.Now:yyyyMMdd_HHmmss}")
                        If String.IsNullOrWhiteSpace(name) Then
                            MessageBox.Show("已取消")
                            Return
                        End If
                        For Each c In IO.Path.GetInvalidFileNameChars()
                            name = name.Replace(c, "_"c)
                        Next
                        _currentTemplateName = name
                    End If

                    Dim config As New TemplateConfig

                    With config
                        '.CameraDeviceId = _selectedCameraId ' 記錄建模相機
                        .CameraDeviceId = _templateCameraId ' 記錄建模相機
                        .Threshold = ThresholdSlider.Value
                        .MatchMethod = MatchMethodBox.SelectedIndex
                        .RoiX = _roi.X
                        .RoiY = _roi.Y
                        .RoiW = _roi.Width
                        .RoiH = _roi.Height
                        .EnableOcr = True
                        .OcrExpectedText = RoiText.Text
                        .EnableBarcode = True
                        .BarcodeExpectedText = ResultText.Text
                        .PyramidLevel = CInt(PyramidSlider.Value)
                        .MinArea = CInt(MinAreaSlider.Value)
                        .CannyLow = CInt(CannyLowSlider.Value)
                        .CannyHigh = CInt(CannyHighSlider.Value)
                        .AngleMin = AngleMinSlider.Value
                        .AngleMax = AngleMaxSlider.Value
                        .AngleStep = AngleStepSlider.Value
                    End With

                    ' 儲存到 Templates/{_currentTemplateName}/cam{slot}/
                    Dim path = TemplateManager.SaveTemplate(_templateMat, config, _currentTemplateName, _selectedCameraSlot)
                    If String.IsNullOrWhiteSpace(path) Then Return

                    Dim snapshot As New TemplateSnapshot

                    With snapshot
                        .TemplatePath = path
                        .CameraDeviceId = config.CameraDeviceId ' 依建模相機
                        .Threshold = config.Threshold
                        .MatchMethod = config.MatchMethod
                        .RoiX = config.RoiX
                        .RoiY = config.RoiY
                        .RoiW = config.RoiW
                        .RoiH = config.RoiH
                        .EnableOcr = config.EnableOcr
                        .OcrExpectedText = config.OcrExpectedText
                        .EnableBarcode = config.EnableBarcode
                        .BarcodeExpectedText = config.BarcodeExpectedText
                        .PyramidLevel = config.PyramidLevel
                        .MinArea = config.MinArea
                        .CannyLow = config.CannyLow
                        .CannyHigh = config.CannyHigh
                        .AngleMin = config.AngleMin
                        .AngleMax = config.AngleMax
                        .AngleStep = config.AngleStep
                    End With

                    ' 彈出編輯對話窗
                    Dim dlg As New TemplateEditDialog(snapshot, Nothing)
                    dlg.Owner = Application.Current?.MainWindow
                    Dim res = dlg.ShowDialog()

                    If res <> True Then
                        MessageBox.Show("已取消保存")
                        Return
                    End If

                    TemplateSnapshotStore.Save(snapshot)
                    LastTemplateStore.Save(path)

                    MessageBox.Show($"已保存：{_currentTemplateName}/cam{_selectedCameraSlot + 1}")

                End Sub)

    End Sub


    ' =========================
    ' Load template manually
    ' =========================
    Private Sub LoadTemplate_Click(sender As Object, e As RoutedEventArgs)

        SafeRun(Sub()

                    Dim data = TemplateManager.LoadTemplate()
                    If data Is Nothing Then Return

                    ApplyTemplate(data.Template, data.Config)

                    LastTemplateStore.Save(data.TemplatePath)

                    MessageBox.Show("模板載入成功")

                End Sub)

    End Sub

    ' =========================
    ' Revise loaded template
    ' =========================
    Private Sub ReviseTemplate_Click(sender As Object, e As RoutedEventArgs)

        SafeRun(Sub()

                    If _templateMat Is Nothing Then
                        MessageBox.Show("請先載入或生成模板")
                        Return
                    End If

                    ' 以磁碟快照為基礎，但用目前 UI 的 OCR/Barcode 期望文字覆蓋，
                    ' 避免剛生成未儲存的模板顯示舊模板的資料（導致「換掉母版內容」的感覺）
                    Dim snapshot = TemplateSnapshotStore.Load()
                    If snapshot Is Nothing Then
                        snapshot = New TemplateSnapshot()
                    End If

                    ' 以目前 UI 狀態覆蓋期望文字（確保對話框顯示的是當前模板的資料）
                    snapshot.OcrExpectedText = RoiText.Text
                    snapshot.BarcodeExpectedText = ResultText.Text

                    ' 彈出編輯對話窗
                    Dim dlg As New TemplateEditDialog(snapshot, TemplateImage.Source)
                    dlg.Owner = Application.Current?.MainWindow
                    Dim res = dlg.ShowDialog()
                    If res <> True Then
                        MessageBox.Show("已取消修訂")
                        Return
                    End If

                    ' 同步修訂後的期望文字回 UI 文字框，
                    ' 確保後續「儲存模板」時能讀取到修訂後的最新值
                    RoiText.Text = If(snapshot.OcrExpectedText, "")
                    ResultText.Text = If(snapshot.BarcodeExpectedText, "")

                    ' 保存修訂後的 snapshot
                    TemplateSnapshotStore.Save(snapshot)
                    MessageBox.Show("模板修訂已保存")

                End Sub)

    End Sub

    ' =========================
    ' Show revision history
    ' =========================
    Private Sub ShowRevisions_Click(sender As Object, e As RoutedEventArgs)

        SafeRun(Sub()

                    Dim snapshot = TemplateSnapshotStore.Load()
                    If snapshot Is Nothing OrElse snapshot.Revisions Is Nothing OrElse snapshot.Revisions.Count = 0 Then
                        MessageBox.Show("無修訂歷史")
                        Return
                    End If

                    Dim msg As New System.Text.StringBuilder()
                    msg.AppendLine("模板修訂歷史：")
                    msg.AppendLine("")

                    For i = snapshot.Revisions.Count - 1 To 0 Step -1
                        Dim rev = snapshot.Revisions(i)
                        Dim dt = New DateTime(DateTimeOffset.FromUnixTimeMilliseconds(rev.Timestamp).Ticks)
                        msg.AppendLine($"#{i + 1} - {dt:yyyy-MM-dd HH:mm:ss}")
                        msg.AppendLine($"  作者: {rev.Author}")
                        msg.AppendLine($"  說明: {rev.Comment}")
                        msg.AppendLine($"  ROI: ({rev.RoiX}, {rev.RoiY}) {rev.RoiW}x{rev.RoiH}")
                        msg.AppendLine("")
                    Next

                    Dim wnd As New System.Windows.Window() With {
                        .Title = "模板修訂歷史",
                        .Width = 600,
                        .Height = 400,
                        .WindowStartupLocation = WindowStartupLocation.CenterOwner,
                        .Owner = Application.Current?.MainWindow
                    }

                    Dim textBox As New TextBox() With {
                        .Text = msg.ToString(),
                        .IsReadOnly = True,
                        .TextWrapping = TextWrapping.Wrap,
                        .VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                        .Foreground = Brushes.Black,
                        .Background = Brushes.White,
                        .Padding = New Thickness(10)
                    }

                    wnd.Content = textBox
                    wnd.ShowDialog()

                End Sub)

    End Sub

    ' =========================
    ' Show or update template edit dialog
    ' =========================
    Private Sub ShowTemplateEditDialog(Optional ocrText As String = "", Optional barcodeText As String = "")

        SafeRun(Sub()

                    Try
                        ' 建立新的 snapshot
                        Dim snapshot As New TemplateSnapshot
                        snapshot.OcrRecognizedText = ocrText
                        snapshot.BarcodeDecodedText = barcodeText
                        snapshot.OcrExpectedText = RoiText.Text
                        snapshot.BarcodeExpectedText = ResultText.Text

                        ' 建立對話窗並顯示
                        Dim dlg As New TemplateEditDialog(snapshot, Nothing)
                        dlg.Owner = Application.Current?.MainWindow
                        Dim res = dlg.ShowDialog()

                        If res = True Then
                            ' 用戶確認了編輯
                            RoiText.Text = dlg.Snapshot.OcrExpectedText
                            ResultText.Text = dlg.Snapshot.BarcodeExpectedText
                        End If

                    Catch ex As Exception
                        Logger.Error("打開模板編輯窗失敗: " & ex.Message)
                    End Try

                End Sub)

    End Sub


    Private Sub ApplyTemplate(mat As Mat, config As TemplateConfig)

        _templateMat = mat

        TemplateImage.Source = ImageConvertHelper.ToBitmap(mat)

        ThresholdSlider.Value = config.Threshold
        MatchMethodBox.SelectedIndex = config.MatchMethod

        _roi = New CvRect(
            config.RoiX,
            config.RoiY,
            config.RoiW,
            config.RoiH
        )

    End Sub

    ' =========================
    ' Run match
    ' =========================
    Private Sub RunTemplateMatch()

        If _matchMat Is Nothing OrElse _templateMat Is Nothing Then Return

        Dim result = TemplateMatcher.Match(
            _matchMat,
            _templateMat,
            ThresholdSlider.Value,
            MatchMethodBox.SelectedIndex
        )

        ScoreText.Text = result.Score.ToString("0.000")
        ResultText.Text = If(result.IsOk, "OK", "NG")

        If result.IsOk Then
            Dim lastTemplatePath = LastTemplateStore.Load()
            If Not String.IsNullOrWhiteSpace(lastTemplatePath) Then
                TemplateTrainingStore.TouchLatestMatched(lastTemplatePath)
            End If
        End If

        ResultImage.Source =
            ImageConvertHelper.ToBitmap(result.ResultImage)

    End Sub

    ' =========================
    ' Reset UI
    ' =========================
    Private Sub ResetUI()

        _autoParamsApplied = False
        RoiCanvas.Children.Clear()
        _roi = New CvRect()

        TemplateImage.Source = Nothing
        ResultImage.Source = Nothing

        ScoreText.Text = ""
        ResultText.Text = "--"

        RoiStatusText.Text = "未選擇"
        TemplateStatusText.Text = "未生成"

    End Sub

    ' OCR
    Private _ocr As PaddleOcrService = AppRuntime.OCR

    Private Sub OcrRegion_Click(sender As Object, e As RoutedEventArgs)

        Try
            If _srcMat Is Nothing Then
                MessageBox.Show("請先載入圖片")
                Return
            End If

            If _roi.Width <= 0 OrElse _roi.Height <= 0 Then
                MessageBox.Show("請先畫ROI")
                Return
            End If

            Dim roiMat As New Mat(_srcMat, _roi)
            ResultImage.Source = ImageConvertHelper.ToBitmap(roiMat)

            Dim result = _ocr.RunRoi(_srcMat, _roi)
            If result Is Nothing Then Return

            Dim text As String = result.Text
            Dim score As Double = result.Score

            If String.IsNullOrWhiteSpace(text) Then
                text = "[OCR EMPTY]"
            End If

            RoiText.Text = text
            ScoreText.Text = score.ToString("F3")

            ShowTemplateEditDialog(ocrText:=text)

        Catch ex As Exception
            MessageBox.Show(ex.Message)
        End Try

    End Sub

#Region "Barcode"
    Private _decoder As BarcodeDecodeService = AppRuntime.Barcode
    Private Sub DecodeRegion_Click(sender As Object, e As RoutedEventArgs)

        Try

            If _srcMat Is Nothing Then
                MessageBox.Show("請先載入圖片")
                Return
            End If

            If _roi.Width <= 0 OrElse _roi.Height <= 0 Then
                MessageBox.Show("請先畫ROI")
                Return
            End If

            Dim text As String = _decoder.RunRoi(_srcMat, _roi)

            If String.IsNullOrWhiteSpace(text) Then
                ResultText.Text = "未識別"
            Else
                ResultText.Text = text
            End If

            ShowTemplateEditDialog(barcodeText:=text)

        Catch ex As Exception
            MessageBox.Show("錯誤：" & ex.Message)
        End Try

    End Sub
#End Region

    ' ========================= Auto Params =========================
    Private Structure AutoTemplateParams
        Public Pyramid As Integer
        Public CannyLow As Integer
        Public CannyHigh As Integer
        Public MinArea As Integer
    End Structure

    ''' <summary>
    ''' 依 ROI 圖像統計自動計算最佳匹配參數（在背景執行緒呼叫）。
    ''' Canny 閾值採用中位數 sigma 規則（lower=0.67×v, upper=1.33×v）。
    ''' </summary>
    Private Function ComputeAutoParams(src As Mat, roi As CvRect) As AutoTemplateParams
        Dim p As New AutoTemplateParams()

        Dim srcArea As Double = CDbl(src.Width) * CDbl(src.Height)
        Dim roiArea As Double = CDbl(roi.Width) * CDbl(roi.Height)
        Dim ratio As Double = If(srcArea > 0, roiArea / srcArea, 0.1)

        ' Pyramid from ROI-to-source area ratio
        If ratio < 0.03 Then
            p.Pyramid = 3
        ElseIf ratio < 0.1 Then
            p.Pyramid = 2
        Else
            p.Pyramid = 1
        End If

        ' Canny from image median pixel (0.33-sigma rule)
        p.CannyLow = 80
        p.CannyHigh = 160
        Try
            Using gray As New Mat()
                Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY)
                Using roiGray As New Mat(gray, roi)
                    Using roiClone = roiGray.Clone()
                        Dim total = CInt(roiClone.Total())
                        If total > 0 Then
                            Dim bytes(total - 1) As Byte
                            System.Runtime.InteropServices.Marshal.Copy(roiClone.Data, bytes, 0, total)
                            Array.Sort(bytes)
                            Dim median As Integer = bytes(total \ 2)
                            If median > 10 Then
                                p.CannyLow = CInt(Math.Max(0, Math.Min(200, 0.67 * median)))
                                p.CannyHigh = CInt(Math.Max(p.CannyLow + 40, Math.Min(255, 1.33 * median)))
                            End If
                        End If
                    End Using
                End Using
            End Using
        Catch ex As Exception
            Logger.Warn("[AutoParams] Canny計算失敗，使用預設值: " & ex.Message)
        End Try

        ' MinArea: 0.1% of ROI pixel count, clamped 30~500
        p.MinArea = CInt(Math.Max(30, Math.Min(500, roiArea * 0.001)))

        Return p
    End Function

    ''' <summary>將自動計算的參數同步到 UI 滑塊（不更動閾值，保持使用者設定值）。</summary>
    Private Sub ApplyAutoParams(ap As AutoTemplateParams)
        PyramidSlider.Value = Math.Max(PyramidSlider.Minimum, Math.Min(PyramidSlider.Maximum, ap.Pyramid))
        CannyLowSlider.Value = Math.Max(CannyLowSlider.Minimum, Math.Min(CannyLowSlider.Maximum, ap.CannyLow))
        CannyHighSlider.Value = Math.Max(CannyHighSlider.Minimum, Math.Min(CannyHighSlider.Maximum, ap.CannyHigh))
        MinAreaSlider.Value = Math.Max(MinAreaSlider.Minimum, Math.Min(MinAreaSlider.Maximum, ap.MinArea))
    End Sub
    ' =========================
    ' Safe run
    ' =========================
    Private Sub SafeRun(action As Action)

        Try
            action()
        Catch ex As Exception
            ExceptionHelper.ShowError(ex)
        End Try

    End Sub

    Public Sub RefreshLanguageUI()

        TxtTitle.Text =
            LanguageManager.T("Algo_Title")

        BtnLoadSource.Content =
            LanguageManager.T("Algo_LoadSource")

        BtnCreateTemplate.Content =
            LanguageManager.T("Algo_CreateTemplate")

        BtnLoadMatch.Content =
            LanguageManager.T("Algo_LoadMatch")

    End Sub

End Class