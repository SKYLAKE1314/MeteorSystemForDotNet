Imports System.IO
Imports Newtonsoft.Json
Imports VAT.Common
Imports VAT.Common.VATJsonObject

Partial Public Class ProcessPage

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
                    Dispatcher.BeginInvoke(Sub() AppRuntime.Home.PlayNoTemplateAlert())
                End If
            Else
                AddLog($"[INFO] 找到模板：{IO.Path.GetFileName(groupPath)}，自動載入")
                Logger.Info($"[StartTask] 載入模板 {groupPath}")
                ' 在 UI 執行緒載入模板快照
                Dispatcher.BeginInvoke(Sub()
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

    Private _pauseTaskRunning As Boolean = False
    Private _resumeTaskRunning As Boolean = False

    Private Async Sub PauseTaskExec(t As TaskData)
        AddLog("PAUSE: " & t.RequestId)
        If _pauseTaskRunning Then Return
        _pauseTaskRunning = True
        Try
            Await ExecuteTask(t)
        Finally
            _pauseTaskRunning = False
        End Try
    End Sub

    Private Async Sub ResumeTaskExec(t As TaskData)
        AddLog("RESUME: " & t.RequestId)
        If _resumeTaskRunning Then Return
        _resumeTaskRunning = True
        Try
            Await ExecuteTask(t)
        Finally
            _resumeTaskRunning = False
        End Try
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

    Private Async Function ExecuteTask(t As TaskData) As Task
        Select Case t.TaskStatus
            Case 0
                ' 状态 0: 启动检测和录制
                _allowDetection = True
                _detectionResults.Clear()
                _currentTaskStartTime = DateTimeOffset.Now.ToUnixTimeMilliseconds()
                _currentArtifactFolder = BuildTaskArtifactFolder(t, _currentTaskStartTime)

                ' 提前啟動匹配相機，避免按下按鈕時相機尚未就緒
                If AppRuntime.Home IsNot Nothing Then
                    AppRuntime.Home.PreWarmMatchCamera()
                End If

                AddLog("[TASK] 0 -> ARM (等待物理按鈕或即時檢測)")
                
                ' 非同步啟動錄影，完全不阻塞背景狀態機或執行緒
                Task.Run(Async Function()
                             Await StartTaskRecordingAsync(t)
                         End Function)

            Case 1
                ' 状态 1: 暂停检测倒计时和录制
                If Not _allowDetection Then
                    AddLog("[TASK] 1 -> PAUSE 失敗：檢測未啟動（尚未收到信號 0）")
                    Return
                End If

                AddLog("[TASK] 1 -> PAUSE (暫停檢測流程與錄製)")

                ' 先暫停檢測流程（記錄當前階段、設 _isPaused=True、播語音）
                If AppRuntime.Home IsNot Nothing Then
                    AppRuntime.Home.PauseDetectionFlow()
                End If

                ' 再暫停錄製
                Await TaskVideoRecorder.Instance.PauseRecordingAsync()
                Logger.Info("[VideoRecorder] 錄製已暫停")

            Case 2
                ' 状态 2: 恢复检测倒计时和录制
                If Not _allowDetection Then
                    AddLog("[TASK] 2 -> RESUME 失敗：檢測未啟動（尚未收到信號 0）")
                    Return
                End If

                ' 先恢復錄製
                If TaskVideoRecorder.Instance.IsPaused Then
                    Await TaskVideoRecorder.Instance.ResumeRecordingAsync()
                    Logger.Info("[VideoRecorder] 錄製已恢復")
                Else
                    Logger.Warn("[VideoRecorder] 收到 2 但錄製並未暫停，跳過恢復")
                End If

                ' 再恢復檢測流程（設 _isPaused=False、播語音）
                If AppRuntime.Home IsNot Nothing Then
                    AppRuntime.Home.ResumeDetectionFlow()
                End If

                AddLog("[TASK] 2 -> RESUME (已恢復檢測流程與錄製)")

            Case 3
                ' 状态 3: 停止录制并发送结果
                AddLog("[TASK] 3 -> END (強制中斷檢測流程並返回結果)")

                Try
                    Await TaskVideoRecorder.Instance.StopRecordingAsync()
                    Logger.Info("[VideoRecorder] 錄製已正常結束並存檔")
                Catch ex As Exception
                    AddLog("[VIDEO] 停止錄影發生異常: " & ex.Message)
                End Try

                If AppRuntime.Home IsNot Nothing Then
                    Dim partialResult = AppRuntime.Home.ForceCompleteCurrentDetection()
                    If partialResult IsNot Nothing AndAlso partialResult.Mat IsNot Nothing Then
                        _detectionResults.Add(partialResult)
                    End If

                    ' 現在圖片和影片都安全了，可以毫不留情地停止流程與相機了喔！
                    Dispatcher.Invoke(Sub() AppRuntime.Home.StopTaskFlow())
                Else
                    CameraService.Instance.StopAll()
                End If

                Try
                    ' 如果完全沒有任何結果，就回傳 NULL
                    If Not _allowDetection OrElse _detectionResults.Count = 0 Then
                        Await SendNullResult(t)
                        Return
                    End If

                    ' 將截至強制中斷為止收集到的所有結果（包含剛剛搶救的圖片）立刻發送並存檔
                    Await SendDetectionResult(t, _detectionResults)
                Finally
                    ' 徹底清空，等待下一次被你喚醒喔...
                    _allowDetection = False
                    _detectionResults.Clear()
                    _currentArtifactFolder = ""
                    _currentTaskStartTime = 0
                    _recordingInfo = Nothing
                End Try
        End Select
    End Function

    Private Async Function SendNullResult(t As TaskData) As Task
        Dim json As New Dictionary(Of String, Object)
        json("requestId") = t.RequestId
        json("stationId") = t.StationId
        json("inspectTime") = DateTimeOffset.Now.ToUnixTimeMilliseconds()
        json("totalInspectedCount") = 0
        json("totalMatchCount") = 0
        json("batchNo") = t.BatchNo
        json("partInspectList") = New List(Of Object)()
        json("metadata") = Nothing

        Await _ws.Broadcast(JsonConvert.SerializeObject(json))
        AddLog($"[WS] NULL Sent : {t.RequestId}")
    End Function

    Private Async Function SendDetectionResult(t As TaskData, results As List(Of DetectionResult)) As Task
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
        AddLog($"[WS] RESULT Sent : {t.RequestId} (总數={totalCount}, 匹配={matchCount}, 检测次數={results.Count})")

        ' 收到 3 時發送錄影完成通知（使用錄影開始時記錄的資訊）
        If _recordingInfo IsNot Nothing Then
            Dim streamJson As New Dictionary(Of String, Object)
            streamJson("requestId") = t.RequestId
            streamJson("stationId") = t.StationId
            streamJson("streamUrl") = _recordingInfo.StreamUrl
            streamJson("streamStartTime") = _recordingInfo.StreamStartTime
            streamJson("streamStatus") = "COMPLETED"
            streamJson("videoFormat") = _recordingInfo.VideoFormat
            streamJson("bitRate") = _recordingInfo.BitRate
            streamJson("metadata") = New With {
                .resolution = _recordingInfo.Resolution,
                .frameRate = _recordingInfo.FrameRate
            }
            Await _ws.Broadcast(JsonConvert.SerializeObject(streamJson))
            AddLog($"[WS] STREAM Sent : {t.RequestId} (COMPLETED)")
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

            _recordingInfo = info   ' 保留供收到 3 時發送完成通知
            AddLog($"[VIDEO] Recording started : {filePath}")
        Catch ex As Exception
            AddLog("[VIDEO] 啟動錄影失敗: " & ex.Message)
        End Try
    End Function

    Private Function ResolveRecordingCameraId() As String
        ' 首先嘗試使用用戶在設定頁中選擇的相機
        If Not String.IsNullOrWhiteSpace(My.Settings.RecordingCameraId) Then
            Return My.Settings.RecordingCameraId
        End If

        ' 回退到第一個相機
        Dim fallback = GetCamId(0)
        If String.IsNullOrWhiteSpace(fallback) Then
            ' 如果第一個相機也不存在，嘗試第二個
            fallback = GetCamId(1)
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

End Class
