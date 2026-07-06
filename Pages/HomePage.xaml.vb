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

    Private _initialized As Boolean = False

    Private _currentMat As Mat

    Private _isAlive As Boolean = True
    Private _isActive As Boolean = False
    Private _isStreaming As Boolean = False

    Private _detectLock As New Object()
    Private _isDetecting As Boolean = False

    Private Enum DetectionFlowStage
        Idle = 0
        Matching = 1
        Barcode = 2
        Ocr = 3
    End Enum

    Private _flowStage As DetectionFlowStage = DetectionFlowStage.Idle
    Private _skipCurrentStageRequested As Boolean = False
    Private _activeDetectionResult As DetectionResult
    Private _activeDetectionItem As DetectionItem

    Private Const VoicePromptMatchCompleteScan As String = "MatchCompletedPleaseScan.wav"
    Private Const VoicePromptDecodeCompleteOcr As String = "DecodeCompletedPleaseOCR.wav"
    Private Const VoicePromptSingleFlowCompleted As String = "SingleFlowCompleted.wav"
    Private Const VoicePromptStageTimeout As String = "StageTimeout.wav"
    Private Const StageTimeoutMs As Integer = 60000
    Private Const StageLoopDelayMs As Integer = 30

    Private _detectCameraId As String = GetCamId(1)
    Private _ocrCameraId As String = GetCamId(1)

    Private _io As IOController

    Private _lastFrameMat As Mat
    Private _lastFrameBitmap As BitmapSource

    Private ReadOnly _ocr As PaddleOcrService =
    AppRuntime.OCR

    ' Page Loaded
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
        "192.168.1.117",
        502,
        1,
        0,
        AppRuntime.IoMode,
        Sub(msg) Logger.Info(msg)
        )

        Await _io.InitializeAsync()

        If AppRuntime.IoMode = IoBoardMode.NONE Then
            Logger.Info("IO 已停用，保留語音播報")
        Else
            Logger.Info("IO 初始化完成")
        End If
        ' 數據交互訂閲

        AddHandler ProcessPage.OnRealtimeTrigger, AddressOf RunDetection

        AddHandler Logger.LogReceived, AddressOf GlobalLogReceived

        ' 啟用並訂閱物理按鈕（單一按鈕）
        Try
            _io.StartDIListener(0) ' DI index 可根據硬體配置調整
            AddHandler _io.ButtonChanged, AddressOf IoButtonChanged
            Logger.Info("物理按鈕監聽已啟用")
        Catch ex As Exception
            Logger.Error("啟用物理按鈕監聽失敗: " & ex.Message)
        End Try

        Logger.Info("HomePage 已載入")

        ' =========================
        ' 初始化相機選擇 ComboBox
        ' =========================
        Try
            CameraManager.Initialize()
            CameraManager.Refresh()
            Dim cameras = CameraManager.GetCachedCameras()
            If cameras IsNot Nothing AndAlso cameras.Count > 0 Then
                CameraComboBox.ItemsSource = cameras
                If Not String.IsNullOrWhiteSpace(_detectCameraId) Then
                    CameraComboBox.SelectedValue = _detectCameraId
                ElseIf cameras.Count > 0 Then
                    CameraComboBox.SelectedIndex = 0
                    _detectCameraId = cameras(0).DeviceId
                End If
                Logger.Info($"相機列表已加載，共 {cameras.Count} 個相機")
            Else
                Logger.Warn("未找到可用的相機設備")
            End If
        Catch ex As Exception
            Logger.Error($"初始化相機列表失敗: {ex.Message}")
        End Try

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
    Private Sub Page_Unloaded(sender As Object, e As RoutedEventArgs) Handles Me.Unloaded
        RemoveHandler CameraService.Instance.FrameArrived, AddressOf OnFrameArrived

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
    Private Sub GlobalLogReceived(level As String, msg As String)

        Dispatcher.Invoke(Sub()

                              ' 這裡你可以：
                              ' 1. 更新本頁 log
                              ' 2. 或丟到共享 log window

                              rtbLog.AppendText($"[{level}] {msg}" & Environment.NewLine)
                              rtbLog.ScrollToEnd()

                          End Sub)

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
    Private Sub UpdateFrame(sender As Object, e As EventArgs)

        Dim frame = CameraService.Instance.GetFrame(GetCamId(1))

        If frame Is Nothing Then Return

        RenderImage.Source = frame

    End Sub
    Private Sub OnCameraChanged()

        Task.Run(Sub()

                     Dim ids = My.Settings.CameraDeviceIds
                     If ids Is Nothing OrElse ids.Count = 0 Then
                         CameraService.Instance.StopAll()
                         Return
                     End If

                     CameraService.Instance.StopAll()

                     ' 重新解析當前檢測相機——用最新設定中的第一個相機
                     Dim newCameraId = GetCamId(1)
                     If Not String.IsNullOrWhiteSpace(newCameraId) Then
                         _detectCameraId = newCameraId
                         CameraService.Instance.StartCamera(newCameraId)
                         Logger.Info($"[Camera] 設定已更新，相機切換為: {newCameraId}")
                     End If

                 End Sub)

    End Sub
End Class
