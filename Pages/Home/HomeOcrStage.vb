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

    Private Async Function WaitOcrResultAsync(snapshot As TemplateSnapshot, timeoutMs As Integer) As Task(Of String)
        Dim ocr = AppRuntime.OCR
        If ocr Is Nothing Then Return ""

        'Dim cameraId = ResolveDetectCameraId()
        ' 臨時
        Dim cameraId = GetCamId(1)
        '
        If String.IsNullOrWhiteSpace(cameraId) Then Return ""
        ' 臨時
        CameraService.Instance.StopAll()
        Thread.Sleep(100)
        '
        CameraService.Instance.StartCamera(cameraId)

        ' 解析期望的OCR文本（支持多個子模板，用;分隔）
        Dim expectedTexts As New List(Of String)
        If Not String.IsNullOrWhiteSpace(snapshot?.OcrExpectedText) Then
            expectedTexts.AddRange(snapshot.OcrExpectedText.Split(";"c).
                Select(Function(t) t.Trim()).
                Where(Function(t) Not String.IsNullOrWhiteSpace(t)).
                ToList())
        End If

        ' 多角度旋轉（與OCR測試原理相同）
        Dim angles() As Double = {-45, 0, 15, 45}

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
                ' 更新預覽畫面（UI執行緒）
                Dim frameCopy = frame
                Dispatcher.Invoke(Sub() RenderImage.Source = frameCopy)

                ' 所有OCR運算放入Task.Run，避免UI卡頓
                Dim result = Await Task.Run(Function()
                                                Dim localBestText As String = ""
                                                Dim localBestScore As Double = 0

                                                Using mat = BitmapSourceToMat(frameCopy)
                                                    Dim roi = ResolveRoi(snapshot, mat)

                                                    For Each angle In angles
                                                        Using rotated = RotateMat(mat, angle)
                                                            Dim ocrResult = ocr.RunRoi(rotated, roi)

                                                            If ocrResult IsNot Nothing AndAlso
                                                               Not String.IsNullOrWhiteSpace(ocrResult.Text) Then

                                                                Logger.Debug($"[OCR] Angle={angle} Text={ocrResult.Text.Trim()} Score={ocrResult.Score:F3}")

                                                                If ocrResult.Score > localBestScore Then
                                                                    localBestScore = ocrResult.Score
                                                                    localBestText = ocrResult.Text.Trim()
                                                                End If

                                                                ' 達到高置信度即可停止繼續嘗試角度
                                                                If ocrResult.Score >= 0.8 Then Exit For
                                                            End If
                                                        End Using
                                                    Next
                                                End Using

                                                Return New With {.Text = localBestText, .Score = localBestScore}
                                            End Function)

                If Not String.IsNullOrWhiteSpace(result.Text) Then
                    ' 檢查是否包含期望的任何子文本
                    If expectedTexts.Count > 0 Then
                        For Each expected In expectedTexts
                            If result.Text.Contains(expected) Then
                                Logger.Info($"[FLOW] OCR 包含匹配成功: 期望={expected}, 識別={result.Text}, Score={result.Score:F3}")
                                Return result.Text
                            End If
                        Next
                    End If

                    ' 更新最高分記錄
                    If result.Score > bestScore Then
                        bestScore = result.Score
                        bestText = result.Text
                    End If

                    ' 未設定期望文本時，達到高置信度即返回
                    If result.Score >= 0.8 AndAlso expectedTexts.Count = 0 Then
                        Return result.Text
                    End If
                End If
            End If

            Await Task.Delay(100) ' OCR 帧間小休，防止多角度重複匹配卡點
        End While

        If Not String.IsNullOrWhiteSpace(bestText) Then Return bestText

        Logger.Warn("[FLOW] OCR 超時")
        Return ""
    End Function
End Class
