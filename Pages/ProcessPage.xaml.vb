Imports System.Reflection.Metadata
Imports System.IO
Imports System.Windows
Imports Newtonsoft.Json
Imports System.Linq
Imports VAT.Common
Imports VAT.Common.VATJsonObject

Partial Public Class ProcessPage

    Private ReadOnly _ws As New WebSocketManager()
    Private ReadOnly _server As New WebSocketServer()

    Private ReadOnly _client As New WebSocketClient()
    Private ReadOnly _router As New TaskRouter()

    Private _taskMap As New Dictionary(Of String, VATJsonObject)

    Private _enableDataReturn As Boolean = False
    Private _enableRealtime As Boolean = False

    Private _endTaskRunning As Boolean = False
    Private _realtimeTimer As System.Timers.Timer

    Public Shared Event OnRealtimeTrigger As Action(Of Action(Of DetectionResult))
    Private _isOnline As Boolean = False

    Private _currentTask As TaskData

    Private _allowDetection As Boolean = False
    Private _detectionResults As New List(Of DetectionResult)()
    Private _currentArtifactFolder As String = ""
    Private _currentTaskStartTime As Long = 0

    Private _realtimeRunning As Boolean = False

    Private _isInitializing As Boolean = False
    Public Sub New()

        _isInitializing = True
        InitializeComponent()

        AddHandler _ws.MessageReceived,
        AddressOf OnMessageReceived
        _router.OnStart = AddressOf StartTask
        _router.OnResume = AddressOf ResumeTask
        _router.OnEnd = AddressOf EndTask


        AddHandler Me.Loaded,
            AddressOf Page_Loaded

    End Sub

    Private Sub Page_Loaded(
    sender As Object,
    e As RoutedEventArgs)


    End Sub

    Private _serverStarted As Boolean = False

    Private Sub StartServer_Click(
    sender As Object,
    e As RoutedEventArgs)

        Try

            If _serverStarted Then
                AddLog("Server 已啟動")
                Return
            End If

            Dim port As Integer =
            Integer.Parse(PortBox.Text)

            AddLog($"Server Started : 0.0.0.0:{port}")

            Task.Run(
            Async Function()

                Await _ws.StartServer(port)

            End Function)

            _serverStarted = True

        Catch ex As Exception

            AddLog(ex.Message)

        End Try

    End Sub

    Public Async Sub AutoStartServer()

        If _serverStarted Then Return

        Try

            Dim port As Integer = Integer.Parse(PortBox.Text)

            Await _ws.StartServer(port)

            _serverStarted = True

            AddLog("Auto Server Started")

        Catch ex As Exception

            AddLog(ex.Message)

        End Try

    End Sub
    Private Sub StopServer_Click(
    sender As Object,
    e As RoutedEventArgs)

        Try

            _ws.StopServer()

            _serverStarted = False

            AddLog("Server Stopped")

        Catch ex As Exception

            AddLog(ex.Message)

        End Try

    End Sub
    Private Async Sub Broadcast_Click(
    sender As Object,
    e As RoutedEventArgs)

        Try

            Await _ws.Broadcast(
            SendBox.Text)

            AddLog(
            "Broadcast : " &
            SendBox.Text)

        Catch ex As Exception

            AddLog(
            ex.Message)

        End Try

    End Sub

    Private Sub ClearLog_Click(
    sender As Object,
    e As RoutedEventArgs)

        LogBox.Items.Clear()

    End Sub
    Private Sub OnMessageReceived(sender As Object, e As WebSocketMessageEventArgs)

        Dispatcher.Invoke(Sub()

                              Try
                                  Dim rawMessage = If(e.Message, "").Trim()

                                  If String.Equals(e.Source, "System", StringComparison.OrdinalIgnoreCase) Then
                                      AddLog($"[WS] {rawMessage}")
                                      Exit Sub
                                  End If

                                  If String.IsNullOrWhiteSpace(rawMessage) Then Exit Sub

                                  ' 只有 JSON 才進入 router，避免連線/提示訊息被誤判成 taskStatus=0
                                  If Not rawMessage.StartsWith("{") AndAlso Not rawMessage.StartsWith("[") Then
                                      AddLog($"[WS:{e.Source}] {rawMessage}")
                                      Exit Sub
                                  End If

                                  Dim msg As New VATJsonObject(rawMessage)

                                  If msg Is Nothing Then Exit Sub

                                  Try
                                      _router.Route(msg)
                                  Catch ex As Exception
                                      ErrorDialogHelper.ShowError("Router Error: " & ex.Message)
                                  End Try

                              Catch ex As Exception
                                  ErrorDialogHelper.ShowError($"Parse Error [{e.Source}] {ex.Message}")
                              End Try

                          End Sub)
        Logger.Info("Receive : " & Me.GetHashCode())
    End Sub

    Private Sub StartTask(t As TaskData)

        AddLog($"START {t.RequestId}")
        AddLog($"Part={t.PartCode}, Supplier={t.SupplierCode}, Count={t.PartCount}")
        AddLog($"BatchNo={t.BatchNo}")

        ' 依 SupplierCode 查找對應模板並自動載入
        If Not String.IsNullOrWhiteSpace(t.SupplierCode) Then
            Dim groupPath = TemplateManager.FindGroupBySupplierCode(t.SupplierCode)
            If String.IsNullOrWhiteSpace(groupPath) Then
                AddLog($"[WARN] 找不到 SupplierCode='{t.SupplierCode}' 的模板，語音告警")
                Logger.Warn($"[StartTask] 找不到 SupplierCode='{t.SupplierCode}' 的模板")
                ' 播報告警音
                If AppRuntime.Home IsNot Nothing Then
                    Dispatcher.Invoke(Sub() AppRuntime.Home.PlayAlert())
                End If
            Else
                AddLog($"[INFO] 找到模板：{IO.Path.GetFileName(groupPath)}，自動載入")
                Logger.Info($"[StartTask] 載入模板 {groupPath}")
                ' 在 UI 執行緒載入模板快照
                Dispatcher.Invoke(Sub()
                                      Try
                                          Dim camDirs = IO.Directory.GetDirectories(groupPath, "cam*").
                                              Where(Function(d) IO.File.Exists(IO.Path.Combine(d, "template.png"))).
                                              ToList()
                                          Dim firstCam = If(camDirs.Count > 0, camDirs(0), groupPath)
                                          Dim data = TemplateManager.LoadTemplate(firstCam)
                                          If data IsNot Nothing Then
                                              Dim snap As New TemplateSnapshot With {
                                                  .TemplatePath = firstCam,
                                                  .CameraDeviceId = data.Config.CameraDeviceId,
                                                  .Threshold = data.Config.Threshold,
                                                  .MatchMethod = data.Config.MatchMethod,
                                                  .RoiX = data.Config.RoiX,
                                                  .RoiY = data.Config.RoiY,
                                                  .RoiW = data.Config.RoiW,
                                                  .RoiH = data.Config.RoiH,
                                                  .EnableOcr = data.Config.EnableOcr,
                                                  .OcrExpectedText = data.Config.OcrExpectedText,
                                                  .EnableBarcode = data.Config.EnableBarcode,
                                                  .BarcodeExpectedText = data.Config.BarcodeExpectedText,
                                                  .PyramidLevel = data.Config.PyramidLevel,
                                                  .MinArea = data.Config.MinArea,
                                                  .CannyLow = data.Config.CannyLow,
                                                  .CannyHigh = data.Config.CannyHigh,
                                                  .AngleMin = data.Config.AngleMin,
                                                  .AngleMax = data.Config.AngleMax,
                                                  .AngleStep = data.Config.AngleStep
                                              }
                                              TemplateSnapshotStore.Save(snap)
                                              LastTemplateStore.Save(firstCam)
                                              AddLog($"[INFO] 模板快照已更新：{IO.Path.GetFileName(groupPath)}")
                                          End If
                                      Catch ex As Exception
                                          AddLog($"[ERROR] 載入模板失敗: {ex.Message}")
                                      End Try
                                  End Sub)
            End If
        Else
            AddLog("[INFO] SupplierCode 為空，不自動查找模板")
        End If

    End Sub

    Private Sub LogTaskStart(t As TaskData)

        AddLog($"START {t.RequestId}")
        AddLog($"Part={t.PartCode}, Supplier={t.SupplierCode}, Count={t.PartCount}")
        AddLog($"BatchNo={t.BatchNo}")

    End Sub

    Private Sub PauseTask(t As TaskData)
        AddLog("PAUSE: " & t.RequestId)
    End Sub

    Private Sub ResumeTask(t As TaskData)
        AddLog("RESUME: " & t.RequestId)
    End Sub
    Private Async Sub EndTask(t As TaskData)

        If _endTaskRunning Then Return

        _endTaskRunning = True

        Try
            Await ExecuteTask(t)
        Finally
            _endTaskRunning = False
        End Try

    End Sub

    Private Sub HandleLog(msg As VATJsonObject)
        AddLog("[LOG] " & msg("msg").ToString())
    End Sub

    Private Sub HandleData(msg As VATJsonObject)

        Dim cam As String = msg("camera")
        Dim score As Double = Double.Parse(msg("score"))

        AddLog($"Camera={cam}, Score={score}")

    End Sub

    Public Sub HandleStatus(msg As VATJsonObject)

        Dim device = msg("device")
        Dim online = Boolean.Parse(msg("online"))

        ' 只記錄在線狀態，不在狀態通知時自動啟動檢測
        _isOnline = online

        AddLog($"{device} Online={online}")

    End Sub
    Private Sub TriggerRealtime()

        If _mode <> RunMode.Realtime Then Return
        If AppRuntime.Home Is Nothing Then Return
        If _allowDetection Then Return

        Dispatcher.Invoke(Sub()

                              AppRuntime.Home.RunDetection(Sub(result)

                                                               _detectionResults.Add(result)

                                                               Dim json As New Dictionary(Of String, Object)

                                                               json("partInspectList") = result.List
                                                               json("imageBase64") = result.ImageBase64

                                                               _ws.Broadcast(JsonConvert.SerializeObject(json))

                                                           End Sub)

                          End Sub)

    End Sub
    ' 數據交互

    Private Sub DataReturn_Checked(sender As Object, e As RoutedEventArgs)
        _mode = RunMode.Mock
    End Sub

    Private Sub DataReturn_Unchecked(sender As Object, e As RoutedEventArgs)
        If _mode = RunMode.Mock Then _mode = RunMode.None
    End Sub

    Private Sub Realtime_Checked(sender As Object, e As RoutedEventArgs)

        If _isInitializing Then Return

        _mode = RunMode.Realtime
        _enableRealtime = True

        TryStartRealtime()

    End Sub

    Private Sub Realtime_Unchecked(sender As Object, e As RoutedEventArgs)

        If _isInitializing Then Return

        If _mode = RunMode.Realtime Then
            _mode = RunMode.None
        End If

        _enableRealtime = False

    End Sub

    Private Sub TryStartRealtime()

        If Not _enableRealtime Then Return
        If _isOnline Then Return
        If _realtimeRunning Then Return   ' ⭐ 防重复

        _realtimeRunning = True

        Task.Run(Async Sub()

                     Await Task.Delay(10000)

                     If Not _enableRealtime OrElse _isOnline Then
                         _realtimeRunning = False
                         Return
                     End If

                     TriggerRealtime()

                     _realtimeRunning = False

                 End Sub)

    End Sub
    ' 逻辑处理入口
    Private Async Function ExecuteTask(t As TaskData) As Task

        Select Case t.TaskStatus

            Case 0
                '==============================
                ' 状态 0: 启动检测和录制
                '==============================
                _allowDetection = True
                _detectionResults.Clear()
                _currentTaskStartTime = DateTimeOffset.Now.ToUnixTimeMilliseconds()
                _currentArtifactFolder = BuildTaskArtifactFolder(t, _currentTaskStartTime)

                AddLog("[TASK] 0 -> ARM (等待物理按鈕或即時檢測)")
                Await StartTaskRecordingAsync(t)

            Case 2
                '==============================
                ' 状态 2: 恢复检测倒计时和录制
                '==============================
                AddLog("[TASK] 2 -> RESUME (恢复检测倒计时和录制)")

                ' 恢复录制
                Await TaskVideoRecorder.Instance.ResumeRecordingAsync()
                Logger.Info("[VideoRecorder] 录制已恢复")

                ' 播报语音提示："检测已恢复"
                ' PlayPromptVoice("DetectionResumed.wav")

                ' 在 HomePage 中也恢复检测流程
                If AppRuntime.Home IsNot Nothing Then
                    AppRuntime.Home.ResumeDetectionFlow()
                End If

            Case 3
                '==============================
                ' 状态 3: 停止录制并发送结果
                '==============================
                AddLog("[TASK] 3 -> END (停止录制)")

                Try
                    ' 停止录制（保留暂停期间外的所有内容）
                    Await TaskVideoRecorder.Instance.StopRecordingAsync()

                    ' 沒有前序 0 或尚未完成檢測時，直接回傳空結果
                    If Not _allowDetection OrElse _detectionResults.Count = 0 Then
                        Await SendNullResult(t)
                        Return
                    End If

                    Await SendDetectionResult(t, _detectionResults)
                Finally
                    _allowDetection = False
                    _detectionResults.Clear()
                    _currentArtifactFolder = ""
                    _currentTaskStartTime = 0
                    If AppRuntime.Home IsNot Nothing Then
                        AppRuntime.Home.StopTaskFlow()
                    Else
                        CameraService.Instance.StopAll()
                    End If
                End Try

        End Select

    End Function
    ' 收到3結束 但沒有結果
    Private Async Function SendNullResult(t As TaskData) As Task

        Dim json As New Dictionary(Of String, Object)

        json("requestId") = t.RequestId
        json("stationId") = t.StationId
        json("inspectTime") =
        DateTimeOffset.Now.ToUnixTimeMilliseconds()

        json("totalInspectedCount") = 0
        json("totalMatchCount") = 0

        json("batchNo") = t.BatchNo

        json("partInspectList") = New List(Of Object)()

        json("metadata") = Nothing

        Await _ws.Broadcast(JsonConvert.SerializeObject(json))

        AddLog($"[WS] NULL Sent : {t.RequestId}")

    End Function
    ' 收到3結束 但有結果

    Private Async Function SendDetectionResult(t As TaskData,
                                           results As List(Of DetectionResult)) As Task

        Dim list As New List(Of Object)

        Dim globalIndex As Integer = 1
        Dim matchCount As Integer = 0
        Dim totalCount As Integer = 0

        ' 累積所有檢測結果中的所有零件
        For Each result As DetectionResult In results

            Dim itemIndex As Integer = 1
            For Each item As DetectionItem In result.List

                If item.resultType = "MATCH" Then
                    matchCount += 1
                End If

                Dim imagePath As String = Nothing
                If Not String.IsNullOrWhiteSpace(result.ImageBase64) Then
                    imagePath = SaveImageToFile(result.ImageBase64, t.RequestId, t.StationId, globalIndex)
                End If

                list.Add(New With {
                .detectionNo = $"DET-{DateTime.Now:yyyyMMdd}-{globalIndex:000}-{t.StationId}-{itemIndex:000}",
                .taskPartName = If(String.IsNullOrWhiteSpace(item.taskPartName), t.PartCode, item.taskPartName),
                .recognizedPartName = If(String.IsNullOrWhiteSpace(item.recognizedPartName), t.PartCode, item.recognizedPartName),
                .collectImageUrl = imagePath,
                .recognizedPartCode = If(String.IsNullOrWhiteSpace(item.recognizedPartCode), t.PartCode, item.recognizedPartCode),
                .resultType = item.resultType,
                .confidence = item.confidence
            })

                globalIndex += 1
                itemIndex += 1
                totalCount += 1

            Next

        Next

        Dim json As New Dictionary(Of String, Object)

        json("requestId") = t.RequestId
        json("stationId") = t.StationId
        json("inspectTime") = DateTimeOffset.Now.ToUnixTimeMilliseconds()
        json("totalInspectedCount") = totalCount
        json("totalMatchCount") = matchCount
        json("batchNo") = t.BatchNo
        json("partInspectList") = list

        json("metadata") = New With {
        .algorithmVersion = "v2.3.1",
        .inspectOrder = "按检测顺序排序",
        .partType = t.PartCode
    }

        ' 發送檢測結果JSON
        Await _ws.Broadcast(JsonConvert.SerializeObject(json))

        AddLog($"[WS] RESULT Sent : {t.RequestId} (总数={totalCount}, 匹配={matchCount}, 检测次数={results.Count})")

        ' 發送視頻流元數據JSON（使用實際錄影資訊）
        Dim recInfo = TaskVideoRecorder.Instance.GetCurrentInfo()
        If recInfo IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(recInfo.StreamUrl) Then
            Dim streamJson As New Dictionary(Of String, Object)
            streamJson("requestId") = t.RequestId
            streamJson("stationId") = t.StationId
            streamJson("streamUrl") = recInfo.StreamUrl
            streamJson("streamStartTime") = recInfo.StreamStartTime
            streamJson("streamStatus") = recInfo.StreamStatus
            streamJson("videoFormat") = recInfo.VideoFormat
            streamJson("bitRate") = recInfo.BitRate
            streamJson("metadata") = New With {.resolution = recInfo.Resolution, .frameRate = recInfo.FrameRate}
            Await _ws.Broadcast(JsonConvert.SerializeObject(streamJson))
            AddLog($"[WS] STREAM Sent : {t.RequestId} -> {recInfo.StreamUrl}")
        End If

    End Function

    Private Function SaveImageToFile(base64 As String, requestId As String, stationId As String, index As Integer) As String

        Try
            Dim normalizedBase64 As String = base64.Trim()

            If normalizedBase64.StartsWith("data:", StringComparison.OrdinalIgnoreCase) Then
                Dim separatorIndex = normalizedBase64.IndexOf(","c)
                If separatorIndex >= 0 Then
                    normalizedBase64 = normalizedBase64.Substring(separatorIndex + 1)
                End If
            End If

            Dim imageBytes = Convert.FromBase64String(normalizedBase64)
            Dim folderPath = _currentArtifactFolder
            If String.IsNullOrWhiteSpace(folderPath) Then
                folderPath = BuildTaskArtifactFolder(New TaskData With {.RequestId = requestId, .PartCode = requestId}, DateTimeOffset.Now.ToUnixTimeMilliseconds())
            End If

            Directory.CreateDirectory(folderPath)

            Dim fileName = $"capture_{DateTime.Now:yyyyMMdd_HHmmssfff}_{index:000}.png"
            Dim filePath = Path.Combine(folderPath, fileName)
            File.WriteAllBytes(filePath, imageBytes)

            AddLog($"[IMG] 已保存: {filePath}")
            Return filePath

        Catch ex As Exception
            AddLog($"[IMG] Save failed: {ex.Message}")
            Return Nothing
        End Try

    End Function

    Private Async Function StartTaskRecordingAsync(t As TaskData) As Task
        Try
            Dim cameraId = ResolveRecordingCameraId()
            If String.IsNullOrWhiteSpace(cameraId) Then
                AddLog("[VIDEO] 未設定錄影相機，略過錄影")
                Return
            End If

            Dim folderPath = _currentArtifactFolder
            If String.IsNullOrWhiteSpace(folderPath) Then
                _currentTaskStartTime = DateTimeOffset.Now.ToUnixTimeMilliseconds()
                folderPath = BuildTaskArtifactFolder(t, _currentTaskStartTime)
                _currentArtifactFolder = folderPath
            End If

            Directory.CreateDirectory(folderPath)
            Dim filePath = Path.Combine(folderPath, $"{SafeFileName(t.RequestId)}-live.mp4")
            Dim info = Await TaskVideoRecorder.Instance.StartRecordingAsync(cameraId, filePath)
            If info Is Nothing Then Return

            ' [DISABLED] Stream start notification removed — do not send stream JSON to client
            'Dim json As New Dictionary(Of String, Object)
            'json("requestId") = t.RequestId
            'json("stationId") = t.StationId
            'json("streamUrl") = info.StreamUrl
            'json("streamStartTime") = info.StreamStartTime
            'json("streamStatus") = info.StreamStatus
            'json("videoFormat") = info.VideoFormat
            'json("bitRate") = info.BitRate
            'json("metadata") = New With {.resolution = info.Resolution, .frameRate = info.FrameRate}
            'Await _ws.Broadcast(JsonConvert.SerializeObject(json))
            'AddLog($"[VIDEO] START Sent : {t.RequestId}")
            AddLog($"[VIDEO] Recording started (suppressed broadcast): {t.RequestId}")
        Catch ex As Exception
            AddLog("[VIDEO] 啟動錄影失敗: " & ex.Message)
        End Try
    End Function

    Private Function ResolveRecordingCameraId() As String
        If Not String.IsNullOrWhiteSpace(My.Settings.RecordingCameraId) Then
            Return My.Settings.RecordingCameraId
        End If

        Dim fallback = GetCamId(1)
        If String.IsNullOrWhiteSpace(fallback) Then
            fallback = GetCamId(0)
        End If
        Return fallback
    End Function

    Private Function BuildTaskArtifactFolder(t As TaskData, startTime As Long) As String
        Dim productCode = SafeFileName(If(String.IsNullOrWhiteSpace(t?.PartCode), t?.RequestId, t.PartCode))
        Dim timeFolder = DateTimeOffset.FromUnixTimeMilliseconds(startTime).LocalDateTime.ToString("yyyyMMdd_HHmmss")
        Dim requestFolder = SafeFileName(If(t?.RequestId, "unknown"))
        Return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DetectionImages", productCode, timeFolder, requestFolder)
    End Function

    Private Function SafeFileName(value As String) As String

        If String.IsNullOrWhiteSpace(value) Then Return "unknown"

        Dim invalidChars = Path.GetInvalidFileNameChars()
        Dim builder As New System.Text.StringBuilder(value.Length)

        For Each ch As Char In value
            If Array.IndexOf(invalidChars, ch) >= 0 Then
                builder.Append("_")
            Else
                builder.Append(ch)
            End If
        Next

        Dim safeValue = builder.ToString().Trim()
        If String.IsNullOrWhiteSpace(safeValue) Then safeValue = "unknown"
        If safeValue.Length > 80 Then safeValue = safeValue.Substring(0, 80)

        Return safeValue

    End Function
    ' 公開方法：供外部（如"即時檢測"按鈕）呼叫來設置檢測結果
    Public Sub SetDetectionResult(result As DetectionResult)

        If Not _allowDetection Then
            AddLog("[DETECT] ARM未啟用，忽略結果")
            Return
        End If

        If result Is Nothing OrElse Not result.IsFinal Then
            AddLog("[DETECT] 收到流程中間結果，等待下一階段")
            Return
        End If

        _detectionResults.Add(result)

        AddLog($"[DETECT] 結果已累積 (檢測次數={_detectionResults.Count}, 當前零件數={result.List.Count})")

    End Sub

    Private Async Function RunDetectionAndSend(t As TaskData) As Task

        AddLog($"[DETECT] Start {t.RequestId}")

        If AppRuntime.Home Is Nothing Then
            AddLog("[DETECT] Home not ready")
            Return
        End If

        Dim result = Await AppRuntime.Home.RunDetectionOnce()

        If result Is Nothing Then
            AddLog("[DETECT] Failed")
            Return
        End If

        If Not result.IsFinal Then
            AddLog($"[DETECT] Stage={result.Stage}，等待下一次觸發")
            Return
        End If

        _detectionResults.Add(result)

        AddLog("[DETECT] Finished")

        For Each item As DetectionItem In result.List

            AddLog(
            $"No={item.detectionNo}, " &
            $"Result={item.resultType}, " &
            $"Score={item.confidence:F3}")

        Next

    End Function
    Private Sub SendMockResult(t As TaskData)

        Dim result As New Dictionary(Of String, Object)

        result("requestId") = t.RequestId
        result("batchNo") = t.BatchNo
        result("totalInspectedCount") = t.PartCount

        Dim list As New List(Of Object)

        'For i = 1 To t.PartCount
        For i = 1 To 2

            list.Add(New With {
                .detectionNo = $"MOCK-{i}",
                .resultType = "MATCH",
                .confidence = 0.99,
                .collectImageUrl = "data:image/jpeg;base64,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"
            })

        Next

        result("partInspectList") = list

        _ws.Broadcast(JsonConvert.SerializeObject(result))

    End Sub

    Private Async Sub Connect_Click(
        sender As Object,
        e As RoutedEventArgs)

        Try

            Await _ws.Connect(
                UrlBox.Text.Trim())

            AddLog(
                "Connected")

        Catch ex As Exception

            AddLog(
                ex.Message)

        End Try

    End Sub

    Private Async Sub Send_Click(
        sender As Object,
        e As RoutedEventArgs)

        Try

            Await _ws.SendToServer(
                SendBox.Text)

            AddLog(
                "Send : " &
                SendBox.Text)

        Catch ex As Exception

            AddLog(
                ex.Message)

        End Try

    End Sub

    Private Async Sub Disconnect_Click(
        sender As Object,
        e As RoutedEventArgs)

        Try

            Await _ws.Disconnect()

            AddLog(
                "Disconnected")

        Catch ex As Exception

            AddLog(
                ex.Message)

        End Try

    End Sub

    Private Sub AddLog(msg As String)

        Dim text = $"{DateTime.Now:HH:mm:ss} {msg}"

        Dispatcher.Invoke(Sub()
                              LogBox.Items.Add(text)
                          End Sub)

        Logger.Info(text)

    End Sub

    Public Sub InitializeFromSettings(realtimeEnabled As Boolean)

        _isInitializing = True

        ' 只同步設定狀態，不在初始化階段自動觸發檢測
        Realtime.IsChecked = realtimeEnabled
        _enableRealtime = realtimeEnabled
        _mode = If(realtimeEnabled, RunMode.Realtime, RunMode.None)

        AddLog($"[INIT] Realtime={realtimeEnabled}")

        _isInitializing = False

    End Sub

    Private Enum RunMode
        None
        Mock
        Realtime
    End Enum

    Private _mode As RunMode = RunMode.None

End Class
