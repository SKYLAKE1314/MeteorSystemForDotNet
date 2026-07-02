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

    Private Function ResolveDetectCameraId() As String
        If Not String.IsNullOrWhiteSpace(_detectCameraId) Then
            Return _detectCameraId
        End If

        Dim fallback = GetCamId(1)
        If String.IsNullOrWhiteSpace(fallback) Then
            fallback = GetCamId(0)
        End If

        If Not String.IsNullOrWhiteSpace(fallback) Then
            _detectCameraId = fallback
            _ocrCameraId = fallback
        End If

        Return fallback
    End Function

    Private Async Function GetDetectFrameAsync() As Task(Of BitmapSource)
        Dim camId = ResolveDetectCameraId()
        If String.IsNullOrWhiteSpace(camId) Then
            Logger.Error("[DETECT] 未設定檢測相機")
            Return Nothing
        End If

        Dim frame = CameraService.Instance.GetFrame(camId)
        If frame IsNot Nothing Then Return frame

        CameraService.Instance.StartCamera(camId)

        For i As Integer = 1 To 20
            Await Task.Delay(50)
            frame = CameraService.Instance.GetFrame(camId)
            If frame IsNot Nothing Then Return frame
        Next

        Logger.Error($"[DETECT] 無法取得相機畫面，CamId={camId}")
        Return Nothing
    End Function

    Private Function TryBeginDetectionSession(triggerSource As String, ByRef stage As DetectionFlowStage) As Boolean
        SyncLock _detectLock
            If Not _isDetecting Then
                _isDetecting = True
                _skipCurrentStageRequested = False
                _flowStage = DetectionFlowStage.Matching
                _activeDetectionResult = New DetectionResult With {
                    .List = New List(Of DetectionItem)
                }
                _activeDetectionItem = New DetectionItem With {
                    .detectionNo = $"AI-{DateTime.Now:yyyyMMdd-HHmmssfff}"
                }
                _activeDetectionResult.List.Add(_activeDetectionItem)
                Logger.Info($"[{triggerSource}] 啟動新的檢測組")
                stage = _flowStage
                Return True
            End If

            stage = _flowStage
            Logger.Info($"[{triggerSource}] 忽略重入觸發，當前階段={stage}")
            Return False
        End SyncLock
    End Function

    Private Sub SetFlowStage(stage As DetectionFlowStage)
        SyncLock _detectLock
            _flowStage = stage
            _skipCurrentStageRequested = False
        End SyncLock
    End Sub

    Private Function IsSkipRequested(stage As DetectionFlowStage) As Boolean
        SyncLock _detectLock
            Return _flowStage = stage AndAlso _skipCurrentStageRequested
        End SyncLock
    End Function

    Private Sub FinishDetection()
        SyncLock _detectLock
            _isDetecting = False
            _skipCurrentStageRequested = False
            _flowStage = DetectionFlowStage.Idle
            _activeDetectionResult = Nothing
            _activeDetectionItem = Nothing
        End SyncLock
    End Sub

    Private Function IsSkippableStageRunning() As Boolean
        SyncLock _detectLock
            Return _isDetecting
        End SyncLock
    End Function

    Public Sub StopTaskFlow()
        Try
            FinishDetection()
            CameraService.Instance.StopAll()
            _isStreaming = False
            Logger.Info("[FLOW] 任務結束，已停止流程與相機")
        Catch ex As Exception
            Logger.Error("[FLOW] 停止流程失敗: " & ex.Message)
        End Try
    End Sub

    Private Sub FillImageBase64(result As DetectionResult)
        If result Is Nothing OrElse result.Mat Is Nothing Then Return

        Dim bmp = MatToBitmapSource(result.Mat)
        Dim encoder As New PngBitmapEncoder()
        encoder.Frames.Add(BitmapFrame.Create(bmp))

        Using ms As New IO.MemoryStream()
            encoder.Save(ms)
            result.ImageBase64 = Convert.ToBase64String(ms.ToArray())
        End Using
    End Sub

    Public Async Function RunDetection(callback As Action(Of DetectionResult)) As Task

        Try
            Logger.Debug($"[DETECT ENTER] {Guid.NewGuid()}")

            Dim result = Await BtnGetImg_Click()
            If result Is Nothing Then Return

            If result.IsFinal Then
                FillImageBase64(result)
                callback(result)
            End If

        Catch ex As Exception
            Logger.Error("RunDetection error: " & ex.Message)
        End Try

    End Function

    Public Async Function RunDetectionOnce() As Task(Of DetectionResult)

        Try
            Dim result = Await BtnGetImg_Click()
            If result Is Nothing Then Return Nothing

            If result.IsFinal Then
                FillImageBase64(result)
            End If

            Return result

        Catch ex As Exception

            Logger.Error(ex.Message)
            Return Nothing

        End Try

    End Function
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

    Private Sub PlayPromptVoice(fileName As String)
        If String.IsNullOrWhiteSpace(fileName) Then Return
        If _io Is Nothing Then
            Logger.Warn($"[VOICE] IOController尚未初始化，無法播報: {fileName}")
            Return
        End If
        Try
            Logger.Info($"[VOICE] 播報開始: {fileName}")
            _io.PlayCustomVoice(fileName)
            Logger.Info($"[VOICE] 播報完成: {fileName}")
        Catch ex As Exception
            Logger.Error($"[VOICE] 播報失敗: {ex.Message}")
        End Try
    End Sub

    Private Function ResolveRoi(snapshot As TemplateSnapshot, src As Mat) As OpenCvSharp.Rect
        If src Is Nothing OrElse src.Empty() Then Return New OpenCvSharp.Rect(0, 0, 1, 1)
        If snapshot Is Nothing Then Return New OpenCvSharp.Rect(0, 0, src.Width, src.Height)

        If snapshot.RoiW <= 0 OrElse snapshot.RoiH <= 0 Then
            Return New OpenCvSharp.Rect(0, 0, src.Width, src.Height)
        End If

        Dim x = Math.Max(0, Math.Min(snapshot.RoiX, src.Width - 1))
        Dim y = Math.Max(0, Math.Min(snapshot.RoiY, src.Height - 1))
        Dim w = Math.Max(1, Math.Min(snapshot.RoiW, src.Width - x))
        Dim h = Math.Max(1, Math.Min(snapshot.RoiH, src.Height - y))

        Return New OpenCvSharp.Rect(x, y, w, h)
    End Function

    Private Async Function WaitBarcodeResultAsync(snapshot As TemplateSnapshot, timeoutMs As Integer) As Task(Of String)
        Dim decoder = AppRuntime.Barcode
        If decoder Is Nothing Then Return ""

        Dim cameraId = ResolveDetectCameraId()
        If String.IsNullOrWhiteSpace(cameraId) Then Return ""
        CameraService.Instance.StartCamera(cameraId)

        Dim sw As New Stopwatch()
        sw.Start()

        While sw.ElapsedMilliseconds < timeoutMs
            If IsSkipRequested(DetectionFlowStage.Barcode) Then
                Logger.Info("[FLOW] 解碼已跳過")
                Return ""
            End If

            Dim frame = CameraService.Instance.GetFrame(cameraId)
            If frame IsNot Nothing Then
                Using mat = BitmapSourceToMat(frame)
                    Dim roi = ResolveRoi(snapshot, mat)
                    Dim text = Await Task.Run(Function() decoder.RunRoi(mat, roi))
                    If Not String.IsNullOrWhiteSpace(text) Then
                        Return text.Trim()
                    End If
                End Using
            End If

            Await Task.Delay(StageLoopDelayMs)
        End While

        Logger.Warn("[FLOW] 解碼超時")
        Return ""
    End Function

    Private Async Function WaitOcrResultAsync(snapshot As TemplateSnapshot, timeoutMs As Integer) As Task(Of String)
        Dim ocr = AppRuntime.OCR
        If ocr Is Nothing Then Return ""

        Dim cameraId = ResolveDetectCameraId()
        If String.IsNullOrWhiteSpace(cameraId) Then Return ""
        CameraService.Instance.StartCamera(cameraId)

        Dim sw As New Stopwatch()
        sw.Start()

        Dim bestText As String = ""
        Dim bestScore As Double = 0

        While sw.ElapsedMilliseconds < timeoutMs
            If IsSkipRequested(DetectionFlowStage.Ocr) Then
                Logger.Info("[FLOW] OCR 已跳過")
                Return ""
            End If

            Dim frame = CameraService.Instance.GetFrame(cameraId)
            If frame IsNot Nothing Then
                Using mat = BitmapSourceToMat(frame)
                    Dim roi = ResolveRoi(snapshot, mat)
                    Dim ocrResult = Await Task.Run(Function() ocr.RunRoi(mat, roi))

                    If ocrResult IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(ocrResult.Text) Then
                        If ocrResult.Score > bestScore Then
                            bestScore = ocrResult.Score
                            bestText = ocrResult.Text.Trim()
                        End If

                        If ocrResult.Score >= 0.8 Then
                            Return ocrResult.Text.Trim()
                        End If
                    End If
                End Using
            End If

            Await Task.Delay(StageLoopDelayMs)
        End While

        If Not String.IsNullOrWhiteSpace(bestText) Then Return bestText

        Logger.Warn("[FLOW] OCR 超時")
        Return ""
    End Function

    Public Async Function BtnGetImg_Click() As Task(Of DetectionResult)

        Try
            Dim frame = Await GetDetectFrameAsync()
            If frame Is Nothing Then Return Nothing

            Logger.Debug($"[DETECT] frame={(frame IsNot Nothing)}")

            Using mat = BitmapSourceToMat(frame)

                Dim templatePath = LastTemplateStore.Load()
                If String.IsNullOrWhiteSpace(templatePath) Then Return Nothing

                Logger.Debug($"[DETECT] template={templatePath}")
                Dim templateName = IO.Path.GetFileName(templatePath)

                Dim stage As DetectionFlowStage = DetectionFlowStage.Idle
                If Not TryBeginDetectionSession("MANUAL", stage) Then
                    Logger.Info($"[FLOW] 檢測已在進行中：{stage}")
                    Return Nothing
                End If

                Dim snapshot = TemplateSnapshotStore.Load()

                Logger.Debug($"[FLOW] Current stage={stage}, EnableBarcode={If(snapshot IsNot Nothing, snapshot.EnableBarcode, False)}, EnableOcr={If(snapshot IsNot Nothing, snapshot.EnableOcr, False)}")

                Dim result = Await Draw_opencv.ProcessAsync(mat, templateName)

                Dispatcher.Invoke(Sub()
                                      RenderImage.Source = result.Mat.ToWriteableBitmap()
                                  End Sub)

                Logger.Info($"Score={result.Score:F3}, OK={result.IsOk}")

                If result.IsOk Then
                    _io.HandleOK()
                Else
                    _io.HandleNG()
                End If

                SyncLock _detectLock
                    If _activeDetectionResult Is Nothing Then
                        _activeDetectionResult = New DetectionResult With {.List = New List(Of DetectionItem)}
                    End If
                    If _activeDetectionItem Is Nothing Then
                        _activeDetectionItem = New DetectionItem With {.detectionNo = $"AI-{DateTime.Now:yyyyMMdd-HHmmssfff}"}
                        _activeDetectionResult.List.Add(_activeDetectionItem)
                    End If
                    _activeDetectionResult.Mat = result.Mat
                    _activeDetectionItem.taskPartName = templateName
                    _activeDetectionItem.resultType = If(result.IsOk, "MATCH", "MISMATCH")
                    _activeDetectionItem.confidence = result.Score
                    _flowStage = DetectionFlowStage.Barcode
                End SyncLock

                ' 不論OK還是NG，匹配後都播報"匹配完成，請掃描"
                PlayPromptVoice(VoicePromptMatchCompleteScan)
                Logger.Info($"[RESULT] 匹配 - OK={result.IsOk}, Score={result.Score:F3}")
                Logger.Info($"[FLOW] 匹配完成，開始解碼")

                Dim code = Await WaitBarcodeResultAsync(snapshot, StageTimeoutMs)
                SyncLock _detectLock
                    If _activeDetectionItem IsNot Nothing Then
                        _activeDetectionItem.recognizedPartCode = code
                    End If
                    _flowStage = DetectionFlowStage.Ocr
                End SyncLock

                ' 如果跳过了条码扫描，记为null
                If IsSkipRequested(DetectionFlowStage.Barcode) Then
                    Logger.Info("[RESULT] 条码 - 已跳过 (null)")
                    code = Nothing
                    SyncLock _detectLock
                        If _activeDetectionItem IsNot Nothing Then
                            _activeDetectionItem.recognizedPartCode = Nothing
                        End If
                    End SyncLock
                Else
                    Logger.Info($"[RESULT] 条码 - {If(String.IsNullOrWhiteSpace(code), "null (超时)", code)}")
                End If

                If String.IsNullOrWhiteSpace(code) Then
                    Dim timeoutOutput As DetectionResult
                    SyncLock _detectLock
                        If _activeDetectionItem IsNot Nothing Then
                            _activeDetectionItem.resultType = "MISMATCH"
                        End If
                        timeoutOutput = _activeDetectionResult
                        If timeoutOutput IsNot Nothing Then
                            timeoutOutput.Mat = result.Mat
                            timeoutOutput.IsFinal = True
                            timeoutOutput.Stage = "BARCODE_TIMEOUT"
                        End If
                    End SyncLock

                    PlayPromptVoice(VoicePromptStageTimeout)
                    PlayPromptVoice(VoicePromptSingleFlowCompleted)
                    Logger.Info("[FLOW] 单次流程结束 (条码超时)")
                    Logger.Info("[SUMMARY] ===================")
                    Logger.Info($"[SUMMARY] 检测编号: {If(_activeDetectionItem IsNot Nothing, _activeDetectionItem.detectionNo, "N/A")}")
                    Logger.Info($"[SUMMARY] 匹配: {If(_activeDetectionItem IsNot Nothing, _activeDetectionItem.resultType, "N/A")} (Score={result.Score:F3})")
                    Logger.Info($"[SUMMARY] 条码: null (超时)")
                    Logger.Info($"[SUMMARY] OCR: 跳过 (无条码)")
                    Logger.Info("[SUMMARY] ===================")
                    FinishDetection()
                    Return timeoutOutput
                End If

                PlayPromptVoice(VoicePromptDecodeCompleteOcr)
                Logger.Info("[FLOW] 開始 OCR")

                Dim name = Await WaitOcrResultAsync(snapshot, StageTimeoutMs)

                ' 如果跳过了OCR，记为null
                If IsSkipRequested(DetectionFlowStage.Ocr) Then
                    Logger.Info("[RESULT] OCR - 已跳过 (null)")
                    name = Nothing
                    SyncLock _detectLock
                        If _activeDetectionItem IsNot Nothing Then
                            _activeDetectionItem.recognizedPartName = Nothing
                        End If
                    End SyncLock
                Else
                    Logger.Info($"[RESULT] OCR - {If(String.IsNullOrWhiteSpace(name), "null (超时)", name)}")
                End If

                Dim finalOutput As DetectionResult
                SyncLock _detectLock
                    If _activeDetectionItem IsNot Nothing Then
                        _activeDetectionItem.recognizedPartName = name
                        If String.IsNullOrWhiteSpace(name) Then
                            _activeDetectionItem.resultType = "MISMATCH"
                        End If
                    End If
                    finalOutput = _activeDetectionResult
                    If finalOutput Is Nothing Then
                        finalOutput = New DetectionResult With {.List = New List(Of DetectionItem)}
                        If _activeDetectionItem IsNot Nothing Then
                            finalOutput.List.Add(_activeDetectionItem)
                        End If
                    End If
                    finalOutput.Mat = result.Mat
                    finalOutput.IsFinal = True
                    finalOutput.Stage = If(String.IsNullOrWhiteSpace(name), "OCR_TIMEOUT", "OCR")
                End SyncLock

                ' 输出完整的流程结果日志
                PlayPromptVoice(VoicePromptSingleFlowCompleted)
                Logger.Info("[FLOW] 单次流程结束")
                Logger.Info("[SUMMARY] ===================")
                Logger.Info($"[SUMMARY] 检测编号: {If(_activeDetectionItem IsNot Nothing, _activeDetectionItem.detectionNo, "N/A")}")
                Logger.Info($"[SUMMARY] 匹配: {If(_activeDetectionItem IsNot Nothing, _activeDetectionItem.resultType, "N/A")} (Score={result.Score:F3})")
                Logger.Info($"[SUMMARY] 条码: {If(String.IsNullOrWhiteSpace(code), "null", code)}")
                Logger.Info($"[SUMMARY] OCR: {If(String.IsNullOrWhiteSpace(name), "null", name)}")
                Logger.Info("[SUMMARY] ===================")

                If String.IsNullOrWhiteSpace(name) Then
                    PlayPromptVoice(VoicePromptStageTimeout)
                End If

                FinishDetection()
                Return finalOutput

            End Using

        Catch ex As Exception
            Logger.Error("Detection error: " & ex.Message)
            Return Nothing
        End Try

    End Function

    ' ⭐ BtnGetImg Click 事件處理器 - "即時檢測"按鈕
    Private Async Sub BtnGetImg_Click_Handler(sender As Object, e As RoutedEventArgs) Handles BtnGetImg.Click

        Try
            Logger.Info("[UI] 即時檢測按鈕 - 按下")

            ' 如果検測正在進行中，按下按鈕會跳過當前階段
            If IsSkippableStageRunning() Then
                SyncLock _detectLock
                    Logger.Info($"[UI] 跳過當前阶段: {_flowStage}")
                    _skipCurrentStageRequested = True
                End SyncLock
                Return
            End If

            Logger.Info("[UI] 即時檢測 - 開始")

            Dim result = Await RunDetectionOnce()

            If result Is Nothing Then
                Logger.Error("[UI] 即時檢測 - 失敗")
                Return
            End If

            If Not result.IsFinal Then
                Logger.Info($"[UI] 即時檢測流程進行中：{result.Stage}")
                Return
            End If

            ' ⭐ 保存結果到 ProcessPage，供 Client 任務使用
            If AppRuntime.Process IsNot Nothing Then
                AppRuntime.Process.SetDetectionResult(result)
                Logger.Info("[UI] 即時檢測結果已發送至 ProcessPage")
            End If

        Catch ex As Exception
            Logger.Error($"[UI] 即時檢測錯誤: {ex.Message}")
        End Try

    End Sub

#End Region
    ' =========================================
    ' IO 按鈕處理
    ' =========================================

    Private Async Sub IoButtonChanged(state As Boolean)

        Try
            If Not state Then Return ' 只在按下時觸發

            Logger.Info("[IO] 物理按鈕按下")

            ' 如果検測正在進行中，按下按鈕會跳過當前階段
            If IsSkippableStageRunning() Then
                SyncLock _detectLock
                    Logger.Info($"[IO] 跳過當前阶段: {_flowStage}")
                    _skipCurrentStageRequested = True
                End SyncLock
                Return
            End If

            Logger.Info("[IO] 物理按鈕 - 啟動檢測")

            Dim result = Await RunDetectionOnce()

            If result Is Nothing Then
                Logger.Error("[IO] 即時檢測失敗或無影像")
                Return
            End If

            If Not result.IsFinal Then
                Logger.Info($"[IO] 即時檢測流程進行中：{result.Stage}")
                Return
            End If

            If AppRuntime.Process IsNot Nothing Then
                AppRuntime.Process.SetDetectionResult(result)
                Logger.Info("[IO] 即時檢測結果已發送至 ProcessPage")
            End If

        Catch ex As Exception
            Logger.Error("IoButtonChanged error: " & ex.Message)
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
    Private Sub Page_Unloaded(sender As Object, e As RoutedEventArgs) Handles Me.Unloaded
        RemoveHandler CameraService.Instance.FrameArrived, AddressOf OnFrameArrived

    End Sub

    Private Sub BtnStart_Click(sender As Object, e As RoutedEventArgs)

        Try

            If _isStreaming Then Return

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

                Dim frame = CameraService.Instance.GetFrame(_ocrCameraId)

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

                Dim ocrCamId = GetCamId(1)
                If String.IsNullOrEmpty(ocrCamId) Then Return
                Dim frame As BitmapSource = CameraService.Instance.GetFrame(ocrCamId)

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

                     Dim ids = My.Settings.CameraDeviceIds

                     If ids Is Nothing OrElse ids.Count = 0 Then Return

                     CameraService.Instance.StopAll()
                     If Not String.IsNullOrWhiteSpace(_detectCameraId) Then
                         CameraService.Instance.StartCamera(_detectCameraId)
                     End If

                 End Sub)

    End Sub

    ' 結果處理函數


    Public Class DetectionResult

        Public Property List As List(Of DetectionItem)
        Public Property ImageBase64 As String
        Public Property Mat As Object
        Public Property Stage As String
        Public Property IsFinal As Boolean

    End Class

    Public Class DetectionItem

        Public Property detectionNo As String

        Public Property taskPartName As String

        Public Property recognizedPartName As String

        Public Property recognizedPartCode As String

        Public Property collectImageUrl As String

        Public Property resultType As String

        Public Property confidence As Double

    End Class

    Private Sub UpdateFrame(sender As Object, e As EventArgs)

        Dim frame = CameraService.Instance.GetFrame(GetCamId(1))

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
