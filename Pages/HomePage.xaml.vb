Imports System.IO
Imports System.Windows
Imports System.Windows.Threading
Imports MetroSystemForDotNet
Imports Microsoft.Web.WebView2.Core
Imports OpenCvSharp
Imports OpenCvSharp.WpfExtensions

Class HomePage

    Private _initialized As Boolean = False

    Private _currentMat As Mat
    ' =========================================
    ' Page Loaded
    ' =========================================
    Private Async Sub Page_Loaded(
        sender As Object,
        e As RoutedEventArgs) Handles Me.Loaded

        If _initialized Then Return

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

        AddHandler CameraManager.CameraChanged, AddressOf OnCameraChanged

        AddHandler CameraService.Instance.FrameArrived,
           AddressOf OnFrameArrived

    End Sub

    ' =========================================
    ' Load Image
    ' =========================================
    Private Sub BtnLoadImage_Click(
        sender As Object,
        e As RoutedEventArgs)

        Try

            Dim path =
                DialogHelper.OpenImage()

            If String.IsNullOrWhiteSpace(path) Then
                Return
            End If

            _currentMat =
                Cv2.ImRead(path)

            ShowRender(_currentMat)

            Logger.Info(
                $"載入圖像：{System.IO.Path.GetFileName(path)}")

        Catch ex As Exception

            MessageBox.Show(ex.Message)

        End Try

    End Sub

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
    Public Sub ShowRender(mat As Mat)

        If mat Is Nothing Then Return

        ' =========================
        ' Load Last Template
        ' =========================
        Dim lastTemplatePath =
        LastTemplateStore.Load()

        If String.IsNullOrWhiteSpace(lastTemplatePath) Then

            RenderImage.Source =
            mat.ToWriteableBitmap()

            Return

        End If

        ' =========================
        ' Load Template
        ' =========================
        Dim data =
        TemplateManager.LoadTemplate(lastTemplatePath)

        If data Is Nothing Then

            RenderImage.Source =
            mat.ToWriteableBitmap()

            Return

        End If

        ' =========================
        ' Match
        ' =========================
        Dim result =
        TemplateMatcher.Match(
            mat,
            data.Template,
            data.Config.Threshold,
            data.Config.MatchMethod
        )

        ' =========================
        ' Render
        ' =========================
        RenderImage.Source =
        result.ResultImage.ToWriteableBitmap()

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

    Private Sub OnFrameArrived(bitmap As BitmapSource)

        Dispatcher.BeginInvoke(Sub()

                                   RenderImage.Source = bitmap

                               End Sub)

    End Sub

    Private Sub Page_Unloaded(sender As Object, e As RoutedEventArgs) Handles Me.Unloaded

        RemoveHandler CameraManager.CameraChanged, AddressOf OnCameraChanged
        RemoveHandler CameraService.Instance.FrameArrived, AddressOf OnFrameArrived

        CameraService.Instance.Stop()   ' ⭐⭐⭐ 必加

        RenderImage.Source = Nothing

    End Sub

    Private Sub BtnStart_Click(
    sender As Object,
    e As RoutedEventArgs)

        Try

            CameraService.Instance.Start()

            Logger.Info("相機已啟動")

        Catch ex As Exception

            MessageBox.Show(ex.Message)

        End Try

    End Sub

    Private Sub BtnStop_Click(
    sender As Object,
    e As RoutedEventArgs)

        Try

            CameraService.Instance.Stop()

            Logger.Info("相機已停止")

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