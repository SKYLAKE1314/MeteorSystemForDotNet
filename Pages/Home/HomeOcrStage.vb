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
        If ocr Is Nothing Then
            Logger.Error("[OCR] AppRuntime.OCR 未設定或初始化失敗")
            Return ""
        End If

        Dim cameraId = _ocrCameraId
        If String.IsNullOrWhiteSpace(cameraId) Then cameraId = _matchCameraId
        If String.IsNullOrWhiteSpace(cameraId) Then cameraId = GetCamId(0)

        If String.IsNullOrWhiteSpace(cameraId) Then
            Logger.Error("[OCR] 無法取得有效的 OCR 相機 ID")
            Return ""
        End If

        Logger.Info($"[FLOW] === 進入 OCR 階段 === 選定相機 CamId={cameraId}, 超時設定={timeoutMs}ms")
        CameraService.Instance.StartCamera(cameraId)

        ' 解析期望的OCR文本
        Dim expectedTexts As New List(Of String)
        If Not String.IsNullOrWhiteSpace(snapshot?.OcrExpectedText) Then
            expectedTexts.AddRange(snapshot.OcrExpectedText.Split(";"c).
        Select(Function(t) t.Trim()).
        Where(Function(t) Not String.IsNullOrWhiteSpace(t)).
        ToList())
        End If

        Dim angles() As Double = {-45, 0, 15, 45}
        Dim sw As New Stopwatch()
        sw.Start()

        Dim bestText As String = ""
        Dim bestScore As Double = 0
        Dim frameCount As Integer = 0

        While sw.ElapsedMilliseconds < timeoutMs
            If IsSkipRequested(DetectionFlowStage.Ocr) Then
                Logger.Info("[FLOW] OCR 已跳過")
                Return Nothing
            End If

            Dim frame = CameraService.Instance.GetFrame(cameraId)
            If frame IsNot Nothing Then
                frameCount += 1
                Dim frameCopy = frame
                Dispatcher.BeginInvoke(Sub() RenderImage.Source = frameCopy)

                Dim result = Await Task.Run(Function()
                                                Dim localBestText As String = ""
                                                Dim localBestScore As Double = 0
                                                Dim isTargetMatched As Boolean = False ' 標記是否已命中期望文本

                                                Using mat = BitmapSourceToMat(frameCopy)
                                                    Dim roi = ResolveRoi(snapshot, mat)

                                                    If roi.Width < 10 OrElse roi.Height < 10 Then
                                                        Logger.Warn($"[OCR] 檢測到異常微小的 ROI ({roi.X},{roi.Y},{roi.Width}x{roi.Height})，自動切換為【全畫面】辨識。")
                                                        roi = New OpenCvSharp.Rect(0, 0, mat.Width, mat.Height)
                                                    End If

                                                    If frameCount = 1 Then
                                                        Logger.Debug($"[OCR] 圖像尺寸={mat.Width}x{mat.Height}, 實際辨識區域 ROI={roi.X},{roi.Y},{roi.Width}x{roi.Height}")
                                                    End If

                                                    For Each angle In angles
                                                        Using rotated = RotateMat(mat, angle)
                                                            Dim ocrResult = ocr.RunRoi(rotated, roi)

                                                            If ocrResult IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(ocrResult.Text) Then
                                                                Dim cleanedText = ocrResult.Text.Trim()
                                                                Logger.Debug($"[OCR] Angle={angle} Text={cleanedText} Score={ocrResult.Score:F3}")
                                                                Dim containsExpected As Boolean = False
                                                                If expectedTexts.Count > 0 Then
                                                                    For Each expected In expectedTexts
                                                                        If cleanedText.Contains(expected) Then
                                                                            containsExpected = True
                                                                            Exit For
                                                                        End If
                                                                    Next
                                                                End If

                                                                If containsExpected Then
                                                                    ' 只要命中期望字元，立刻鎖定結果，拒絕後續角度的覆蓋
                                                                    localBestText = cleanedText
                                                                    localBestScore = ocrResult.Score
                                                                    isTargetMatched = True
                                                                    Exit For ' 跳出 For Each angle
                                                                End If

                                                                ' 若無設定期望字元，或目前角度不包含期望字元，則走原本的「純分數最高」邏輯
                                                                If Not isTargetMatched AndAlso ocrResult.Score > localBestScore Then
                                                                    localBestScore = ocrResult.Score
                                                                    localBestText = cleanedText
                                                                End If

                                                                ' 未設定期望文本時的原本保底優化（高置信度提前中斷）
                                                                If expectedTexts.Count = 0 AndAlso ocrResult.Score >= 0.8 Then
                                                                    Exit For
                                                                End If
                                                            End If
                                                        End Using
                                                    Next
                                                End Using

                                                Return New With {.Text = localBestText, .Score = localBestScore}
                                            End Function)

                ' 外層處理邏輯
                If Not String.IsNullOrWhiteSpace(result.Text) Then
                    If expectedTexts.Count > 0 Then
                        For Each expected In expectedTexts
                            If result.Text.Contains(expected) Then
                                Logger.Info($"[FLOW] OCR 包含匹配成功: 期望={expected}, 識別={result.Text}, Score={result.Score:F3}")
                                Return result.Text
                            End If
                        Next
                    End If

                    If result.Score > bestScore Then
                        bestScore = result.Score
                        bestText = result.Text
                    End If

                    If result.Score >= 0.8 AndAlso expectedTexts.Count = 0 Then
                        Logger.Info($"[FLOW] OCR 高置信度識別成功 (無期望文本): {result.Text} (Score={result.Score:F3})")
                        Return result.Text
                    End If
                End If
            Else
                If frameCount Mod 10 = 0 Then
                    Logger.Warn($"[OCR] 等待相機畫面中... CamId={cameraId}")
                End If
            End If

            Await Task.Delay(100)
        End While

        If Not String.IsNullOrWhiteSpace(bestText) Then
            Logger.Info($"[FLOW] OCR 結束，未完全匹配期望文本，返回歷史最高分結果: {bestText} (Score={bestScore:F3})")
            Return bestText
        End If

        Logger.Warn("[FLOW] OCR 超時，未識別到任何有效文本")
        Return ""
    End Function
End Class
