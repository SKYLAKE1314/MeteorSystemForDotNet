Imports System.IO
Imports System.Text
Imports System.Windows
Imports System.Windows.Threading
Imports MetroSystemForDotNet
Imports Microsoft.Web.WebView2.Core
Imports Microsoft.Win32
Imports OpenCvSharp
Imports OpenCvSharp.WpfExtensions
Imports Cv = OpenCvSharp
Class HomePage

    Private _initialized As Boolean = False

    Private _currentMat As Mat

    Private _isAlive As Boolean = True
    Private _isActive As Boolean = False
    Private _isStreaming As Boolean = False
    Public Sub RunDetection()

        BtnGetImg_Click(Nothing, Nothing)

    End Sub
    ' =========================================
    ' Page Loaded
    ' =========================================
    Private Async Sub Page_Loaded(
        sender As Object,
        e As RoutedEventArgs) Handles Me.Loaded
        AddHandler LanguageManager.LanguageChanged, AddressOf RefreshLanguageUI

        If _initialized Then
            ' ⭐ 回來時補訂閱（關鍵）
            If _isStreaming Then
                AddHandler CameraService.Instance.FrameArrived, AddressOf OnFrameArrived
            End If

            Return
        End If

        _initialized = True

        Logger.SetWpfRichTextBox(rtbLog)

        _io = New IOController(
        "192.168.0.10",
        502,
        1,
        0,
        IoBoardMode.IO,
        Sub(msg) Logger.Info(msg)
    )

        Await _io.InitializeAsync()

        Logger.Info("IO 初始化完成")
        ' 數據交互訂閲

        AddHandler ProcessPage.OnRealtimeTrigger, AddressOf RunDetection

        AddHandler Logger.LogReceived, AddressOf GlobalLogReceived

        Logger.Info("HomePage 已載入")

        ' =========================
        ' Live2D Path
        ' =========================
        'Dim live2dPath As String =
        '    Path.Combine(
        '        AppDomain.CurrentDomain.BaseDirectory,
        '        "UI",
        '        "live2d")

        'Logger.Info(
        '    "Live2D SubSysPath: " & live2dPath)

        _isStreaming = False
    End Sub

    ' =========================================
    ' Load Image
    ' =========================================
    Private Async Sub BtnLoadImage_Click(
    sender As Object,
    e As RoutedEventArgs)

        Try

            Dim path = DialogHelper.OpenImage()
            If String.IsNullOrWhiteSpace(path) Then Return

            Dim mat = Cv.Cv2.ImRead(path)
            _currentMat = mat

            Dim templatePath = LastTemplateStore.Load()
            If String.IsNullOrWhiteSpace(templatePath) Then Return

            Dim templateName = IO.Path.GetFileName(templatePath)

            Dim result = Await Draw_opencv.ProcessAsync(mat, templateName)

            RenderImage.Source =
            result.Mat.ToWriteableBitmap()

            Logger.Info($"Score={result.Score:F3}, OK={result.IsOk}")

            If _io IsNot Nothing Then
                Dim snapshot = TemplateSnapshotStore.Load()
                If snapshot IsNot Nothing Then
                    _io.TriggerByScore(result.Score, snapshot.Threshold)
                End If
            End If

        Catch ex As Exception
            ErrorDialogHelper.ShowError("ROI錯誤: " & ex.Message)
        End Try

    End Sub
#Region "相機觸發"
    Private _io As IOController
    Private Async Sub BtnGetImg_Click(sender As Object, e As RoutedEventArgs)

        Try
            Dim frame = CameraService.Instance.LatestFrame
            If frame Is Nothing Then Return

            Dim mat = BitmapSourceToMat(frame)

            Dim templatePath = LastTemplateStore.Load()
            If String.IsNullOrWhiteSpace(templatePath) Then Return

            Dim templateName = IO.Path.GetFileName(templatePath)

            Dim result = Await Draw_opencv.ProcessAsync(mat, templateName)

            RenderImage.Source = result.Mat.ToWriteableBitmap()

            Logger.Info($"Score={result.Score:F3}, OK={result.IsOk}")

            ' ⭐ 核心：觸發 IO 控制
            If _io IsNot Nothing Then
                Dim snapshot = TemplateSnapshotStore.Load()
                If snapshot IsNot Nothing Then
                    Logger.Info("IO CALL START")
                    _io.TriggerByScore(result.Score, snapshot.Threshold)
                    Logger.Info("IO CALL END")
                End If
            End If

        Catch ex As Exception
            ErrorDialogHelper.ShowError("ROI錯誤: " & ex.Message)
        End Try

    End Sub

#End Region
    ' =========================================
    ' Clear
    ' =========================================
    Private Sub BtnClear_Click(
        sender As Object,
        e As RoutedEventArgs)

        Try

            _currentMat = Nothing

            RenderImage.Source = Nothing

            rtbLog.Document.Blocks.Clear()

            Logger.Info("已清空")

        Catch ex As Exception

            MessageBox.Show(ex.Message)

        End Try

    End Sub

    ' =========================================
    ' Show Render
    ' =========================================
    Public Async Sub ShowRender(mat As Mat)

        If mat Is Nothing Then Return

        Try

            ' =========================
            ' Template Name
            ' =========================
            Dim templatePath = LastTemplateStore.Load()

            If String.IsNullOrWhiteSpace(templatePath) Then
                RenderImage.Source = mat.ToWriteableBitmap()
                Return
            End If

            Dim templateName = IO.Path.GetFileName(templatePath)

            ' =========================
            ' Get Template From Cache
            ' =========================
            Dim data = TemplateCache.GetTemplate(templateName)

            If data Is Nothing Then
                Logger.Warn($"模板不存在: {templateName}")
                RenderImage.Source = mat.ToWriteableBitmap()
                Return
            End If

            ' =========================
            ' Match (Async)
            ' =========================
            Dim result = Await TemplateMatcher.MatchAsync(
            mat,
            data.Template,
            data.Config.Threshold,
            data.Config.MatchMethod)

            If result Is Nothing Then Return

            ' =========================
            ' Render Overlay (畫框 + 分數)
            ' =========================
            Dim display = result.ResultImage.Clone()

            ' --- score text ---
            Dim text As String =
            $"Score: {result.Score:F3}"

            Dim org As New OpenCvSharp.Point(20, 40)

            Cv2.PutText(
            display,
            text,
            org,
            HersheyFonts.HersheySimplex,
            1.0,
            Scalar.Yellow,
            2)

            ' =========================
            ' UI Update
            ' =========================
            RenderImage.Source =
            display.ToWriteableBitmap()

            ' =========================
            ' Log
            ' =========================
            Logger.Info(
            $"Match OK={result.IsOk}, Score={result.Score:F3}")

        Catch ex As Exception

            Logger.Error($"ShowRender error: {ex.Message}")
            RenderImage.Source = mat.ToWriteableBitmap()

        End Try

    End Sub

    Private Sub GlobalLogReceived(level As String, msg As String)

        Dispatcher.Invoke(Sub()

                              ' 這裡你可以：
                              ' 1. 更新本頁 log
                              ' 2. 或丟到共享 log window

                              rtbLog.AppendText($"[{level}] {msg}" & Environment.NewLine)
                              rtbLog.ScrollToEnd()

                          End Sub)

    End Sub
    Private _lastFrameMat As Mat
    Private _lastFrameBitmap As BitmapSource
    Private Sub OnFrameArrived(bitmap As BitmapSource)

        If RenderImage Is Nothing Then Return

        RenderImage.Dispatcher.BeginInvoke(Sub()

                                               If RenderImage Is Nothing Then Return

                                               RenderImage.Source = bitmap

                                               ' ⭐ 保存最後一幀（UI層）
                                               _lastFrameBitmap = bitmap

                                           End Sub)

    End Sub
    Private Sub Page_Unloaded(sender As Object, e As RoutedEventArgs) Handles Me.Unloaded
        RemoveHandler CameraService.Instance.FrameArrived, AddressOf OnFrameArrived

    End Sub

    Private Sub BtnStart_Click(sender As Object, e As RoutedEventArgs)

        Try

            If _isStreaming Then Return

            AddHandler CameraService.Instance.FrameArrived, AddressOf OnFrameArrived

            CameraService.Instance.Start()

            _isStreaming = True

            Logger.Info("相機已啟動")

        Catch ex As Exception
            MessageBox.Show(ex.Message)
        End Try

    End Sub

    Private Sub BtnStop_Click(sender As Object, e As RoutedEventArgs)

        Try

            If Not _isStreaming Then Return

            RemoveHandler CameraService.Instance.FrameArrived, AddressOf OnFrameArrived

            CameraService.Instance.Stop()

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
#Region "清晰度評估暨OCR"
    Private ReadOnly _ocr As PaddleOcrService =
    AppRuntime.OCR
    Private Async Sub BtnLaplacian_Click(
    sender As Object,
    e As RoutedEventArgs)

        Try

            Dim bestFrame As BitmapSource = Nothing
            Dim bestScore As Double = Double.MinValue

            Dim sb As New StringBuilder()
            Dim sw As New Stopwatch()
            sw.Start()

            ' 1. Laplacian 選最佳幀
            While sw.ElapsedMilliseconds < 3000

                Dim frame = CameraService.Instance.LatestFrame

                If frame IsNot Nothing Then

                    Dim score = Laplacian.GetScore(frame)

                    sb.AppendLine($"Score={score:F2}")

                    If score > bestScore Then
                        bestScore = score
                        bestFrame = frame.Clone()
                    End If

                End If

                Await Task.Delay(33)

            End While

            Logger.Debug("===== Laplacian Result =====")
            Logger.Debug(sb.ToString())
            Logger.Debug($"Best laplacian Score={bestScore:F2}")

            If bestFrame Is Nothing Then Return

            RenderImage.Source = bestFrame

            Dim bestMat =
            BitmapSourceConverter.ToMat(bestFrame)

            Dim roi As New OpenCvSharp.Rect(
            0,
            0,
            bestMat.Width,
            bestMat.Height)

            Dim ocrResult = Await Task.Run(Function()

                                               Dim angles() As Double =
    {
        -15, -10, -5,
        0,
        5, 10, 15
    }

                                               Dim bestText As String = ""
                                               Dim bestOcrScore As Double = 0
                                               Dim bestAngle As Double = 0

                                               For Each angle In angles

                                                   Using rotated = RotateMat(bestMat, angle)

                                                       Dim result = _ocr.RunRoi(rotated, roi)

                                                       If result IsNot Nothing Then

                                                           Logger.Debug(
                    $"Angle={angle} Text={result.Text} Score={result.Score:F3}")

                                                           If result.Score > bestOcrScore Then

                                                               bestOcrScore = result.Score
                                                               bestText = result.Text
                                                               bestAngle = angle

                                                           End If

                                                           If result.Score >= 0.9 Then
                                                               Exit For
                                                           End If

                                                       End If

                                                   End Using

                                               Next

                                               ' 沒有 0.9 就用最高分
                                               Return New With {
        .Text = bestText,
        .Score = bestOcrScore,
        .Angle = bestAngle
    }

                                           End Function)

            Logger.Debug("===== OCR Result =====")
            Logger.Debug($"Text={ocrResult.Text}")
            Logger.Debug($"Score={ocrResult.Score:F3}")
            Logger.Debug($"Angle={ocrResult.Angle}")

            MessageBox.Show(
            $"OCR結果：{ocrResult.Text}" &
            vbCrLf &
            $"置信度：{ocrResult.Score:F3}" &
            vbCrLf &
            $"最佳角度：{ocrResult.Angle}")

        Catch ex As Exception

            ErrorDialogHelper.ShowError(
                "清晰度評估失敗：" &
                vbCrLf &
                ex.Message)
        End Try
    End Sub
    Private Function RotateMat(
    src As Mat,
    angle As Double) As Mat

        Dim center As New Point2f(
        src.Width / 2.0F,
        src.Height / 2.0F)

        Dim matrix =
        Cv2.GetRotationMatrix2D(
            center,
            angle,
            1.0)

        Dim dst As New Mat()

        Cv2.WarpAffine(
        src,
        dst,
        matrix,
        src.Size(),
        InterpolationFlags.Linear,
        BorderTypes.Constant,
        Scalar.White)

        Return dst

    End Function
#End Region

    Private Async Sub BtnOcrTest_Click(sender As Object, e As RoutedEventArgs)

        Try

            Dim timeoutMs As Integer = 5000
            Dim sw As New Stopwatch()
            sw.Start()

            Dim bestText As String = ""
            Dim bestScore As Double = 0
            Dim bestAngle As Double = 0

            'Dim angles() As Double = {-135， -90, -45, -15, 0, 15, 45, 90, 135}
            Dim angles() As Double = {-45, 0, 15, 45}

            While sw.ElapsedMilliseconds < timeoutMs

                Dim frame As BitmapSource = CameraService.Instance.LatestFrame

                If frame IsNot Nothing Then

                    RenderImage.Dispatcher.Invoke(Sub()
                                                      RenderImage.Source = frame
                                                  End Sub)

                    Dim mat = BitmapSourceConverter.ToMat(frame)
                    Dim roi As New OpenCvSharp.Rect(0, 0, mat.Width, mat.Height)

                    Dim result = Await Task.Run(Function()

                                                    Dim localBestText As String = ""
                                                    Dim localBestScore As Double = 0
                                                    Dim localBestAngle As Double = 0

                                                    For Each angle In angles

                                                        Using rotated = RotateMat(mat, angle)

                                                            Dim ocr = _ocr.RunRoi(rotated, roi)

                                                            If ocr IsNot Nothing Then

                                                                Logger.Debug(
                                                                 $"Angle={angle} Text={ocr.Text} Score={ocr.Score:F3}")

                                                                If ocr.Score > localBestScore Then
                                                                    localBestScore = ocr.Score
                                                                    localBestText = ocr.Text
                                                                    localBestAngle = angle
                                                                End If

                                                                If ocr.Score >= 0.8 Then
                                                                    Exit For
                                                                End If

                                                            End If

                                                        End Using

                                                    Next

                                                    Return New With {
                                                     .Text = localBestText,
                                                     .Score = localBestScore,
                                                     .Angle = localBestAngle
                                                 }

                                                End Function)

                    If result.Score > bestScore Then
                        bestScore = result.Score
                        bestText = result.Text
                        bestAngle = result.Angle
                    End If

                    If bestScore >= 0.8 Then Exit While

                End If

                Await Task.Delay(300)

            End While

            Logger.Debug("===== FINAL =====")
            Logger.Debug($"Text={bestText}")
            Logger.Debug($"Score={bestScore:F3}")
            Logger.Debug($"Angle={bestAngle}")

            MessageBox.Show(
            $"OCR結果：{bestText}" & vbCrLf &
            $"置信度：{bestScore:F3}" & vbCrLf &
            $"角度：{bestAngle}")

        Catch ex As Exception
            ErrorDialogHelper.ShowError("OCR失敗：" & ex.Message)
        End Try

    End Sub

    Private Sub OnCameraChanged()

        Task.Run(Sub()

                     CameraService.Instance.Stop()
                     CameraService.Instance.Start()

                 End Sub)

    End Sub
    Private Sub UpdateFrame(sender As Object, e As EventArgs)

        Dim frame = CameraService.Instance.LatestFrame

        If frame Is Nothing Then Return

        RenderImage.Source = frame

    End Sub
    Public Sub RefreshLanguageUI()

        BtnLoadImage.Content = LanguageManager.T("Home_BtnLoadImage")
        BtnClear.Content = LanguageManager.T("Home_BtnClear")
        BtnStart.Content = LanguageManager.T("Home_BtnStart")
        BtnStop.Content = LanguageManager.T("Home_BtnStop")
        BtnGetImg.Content = LanguageManager.T("Home_BtnGetImg")
        BtnSave.Content = LanguageManager.T("Home_BtnSave")
        BtnLaplacian.Content = LanguageManager.T("Home_BtnLaplacian")

    End Sub
End Class