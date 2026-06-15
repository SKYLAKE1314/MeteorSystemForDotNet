Imports System.IO
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

    ' =========================================
    ' Page Loaded
    ' =========================================
    Private Async Sub Page_Loaded(
        sender As Object,
        e As RoutedEventArgs) Handles Me.Loaded

        If _initialized Then
            ' ⭐ 回來時補訂閱（關鍵）
            If _isStreaming Then
                AddHandler CameraService.Instance.FrameArrived, AddressOf OnFrameArrived
            End If

            Return
        End If

        _initialized = True

        Logger.SetWpfRichTextBox(rtbLog)

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

        Catch ex As Exception
            MessageBox.Show(ex.Message)
        End Try

    End Sub
#Region "相機觸發"
    Private Async Sub BtnGetImg_Click(sender As Object, e As RoutedEventArgs)

        Try
            Dim frame = CameraService.Instance.LatestFrame
            If frame Is Nothing Then Return

            Dim mat = BitmapSourceToMat(frame)

            Dim templatePath = LastTemplateStore.Load()
            If String.IsNullOrWhiteSpace(templatePath) Then Return

            Dim templateName = IO.Path.GetFileName(templatePath)

            Dim result = Await Draw_opencv.ProcessAsync(mat, templateName)

            RenderImage.Source =
                result.Mat.ToWriteableBitmap()

            Logger.Info($"Score={result.Score:F3}, OK={result.IsOk}")

        Catch ex As Exception
            MessageBox.Show("ROI錯誤" + ex.Message)
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

End Class