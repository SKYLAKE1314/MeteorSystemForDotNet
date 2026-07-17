Imports Newtonsoft.Json
Imports VAT.Common

Partial Public Class ProcessPage

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

    Private Sub TryStartRealtime()
        If Not _enableRealtime Then Return
        If _isOnline Then Return
        If _realtimeRunning Then Return   ' ⭐ 防重複

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
            AddLog($"No={item.detectionNo}, Result={item.resultType}, Score={item.confidence:F3}")
        Next
    End Function

    Private Sub SendMockResult(t As TaskData)
        Dim result As New Dictionary(Of String, Object)
        result("requestId") = t.RequestId
        result("batchNo") = t.BatchNo
        result("totalInspectedCount") = t.PartCount

        Dim list As New List(Of Object)
        For i = 1 To 2
            list.Add(New With {
                .detectionNo = $"MOCK-{i}",
                .resultType = "MATCH",
                .confidence = 0.99,
                .collectImageUrl = "data:image/jpeg;base64,/9j/4AAQSkZJRgABAQAAAQABAAD/4gJASUNDX1BST0ZJTEUAAQEAAAIwAAAAAAIQAABtbnRyUkdCIFhZWiAAAAAAAAAAAAAAAABhY3NwAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAQAA9tYAAQAAAADTLQAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAlkZXNjAAAA8AAAAHRyWFlaAAABZAAAABRnWFlaAAABeAAAABRiWFlaAAABjAAAABRyVFJDAAABoAAAAChnVFJDAAABoAAAAChiVFJDAAABoAAAACh3dHB0AAAByAAAABRjcHJ0AAAB3AAAAFRtbHVjAAAAAAAAAAEAAAAMZW5VUwAAAFgAAAAcAHMAUgBHAEIAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAFhZWiAAAAAAAABvogAAOPUAAAOQWFlaIAAAAAAAAGKZAAC3hQAAGNpYWVogAAAAAAAAJKAAAA+EAAC2z3BhcmEAAAAAAAQAAAACZmYAAPKnAAANWQAAE9AAAApbAAAAAAAAAABYWVogAAAAAAAA9tYAAQAAAADTLW1sdWMAAAAAAAAAAQAAAAxlblVTAAAAOAAAABwARwBvAG8AZwBsAGUAIABJAG4AYwAuACAAMgAwADEANgAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAP/bAEMABgQFBgUEBgYFBgcHBggKEAoKCQkKFA4PDBAXFBgYFxQWFhodJR8aGyMcFhYgLCAjJicpKikZHy0wLSgwJSgpKP/bAEMBBwcHCggKEwoKEygaFhooKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKP/AABEIAoACgAMBIgACEQEDEQH/xAAcAAABBQEBAQAAAAAAAAAAAAAAAQMEBQYCBwj/xABGEAABAwMCAwUFBQYGAQQCAg密集模糊匹配}"
            })
        Next

        result("partInspectList") = list
        _ws.Broadcast(JsonConvert.SerializeObject(result))
    End Sub

End Class
