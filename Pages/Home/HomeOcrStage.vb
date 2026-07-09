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

        ' 解析期望的 OCR 文本
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

                ' ─── 核心非同步辨識運算 ───
                Dim result = Await Task.Run(Function()
                                                Dim localBestText As String = ""
                                                Dim localBestScore As Double = 0

                                                Using mat = BitmapSourceToMat(frameCopy)
                                                    Dim roi = ResolveRoi(snapshot, mat)

                                                    If roi.Width < 10 OrElse roi.Height < 10 Then
                                                        Logger.Warn($"[OCR] 檢測到異常微小的 ROI ({roi.X},{roi.Y},{roi.Width}x{roi.Height})，自動切換為【全畫面】辨識。")
                                                        roi = New OpenCvSharp.Rect(0, 0, mat.Width, mat.Height)
                                                    End If

                                                    If frameCount = 1 Then
                                                        Logger.Debug($"[OCR] 圖像尺寸={mat.Width}x{mat.Height}, 實際辨識區域 ROI={roi.X},{roi.Y},{roi.Width}x{roi.Height}")
                                                    End If

                                                    ' 【核心效能優化 1】先截取 ROI 局部影像，避免對 5120x3840 的大圖做旋轉
                                                    Using roiMat = New Cv.Mat(mat, roi)
                                                        For Each angle In angles
                                                            ' 【核心效能優化 2】只旋轉微小的局部影像，速度提升數百倍！
                                                            Using rotatedRoi = RotateMat(roiMat, angle)
                                                                ' 建立適用於旋轉後局部影像的全滿新 ROI 矩形
                                                                Dim fullRoi = New OpenCvSharp.Rect(0, 0, rotatedRoi.Width, rotatedRoi.Height)
                                                                Dim ocrResult = ocr.RunRoi(rotatedRoi, fullRoi)

                                                                If ocrResult IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(ocrResult.Text) Then
                                                                    Dim cleanedText = ocrResult.Text.Trim()
                                                                    Logger.Debug($"[OCR] Angle={angle} Text={cleanedText} Score={ocrResult.Score:F3}")

                                                                    ' 【需求變更】只要設定了期望字串，且目前辨識結果包含它，立刻無視置信度直接回傳
                                                                    If expectedTexts.Count > 0 Then
                                                                        For Each expected In expectedTexts
                                                                            If cleanedText.Contains(expected) Then
                                                                                Logger.Info($"[FLOW] OCR 命中模板期望文本 (無視置信度): 期望={expected}, 識別={cleanedText}")
                                                                                ' 標記 IsMatched = True 讓外層知道可以立刻早停
                                                                                Return New With {.Text = cleanedText, .Score = ocrResult.Score, .IsMatched = True}
                                                                            End If
                                                                        Next
                                                                    End If

                                                                    ' 若無期望文本，則走原本的「最高分保底」邏輯
                                                                    If ocrResult.Score > localBestScore Then
                                                                        localBestScore = ocrResult.Score
                                                                        localBestText = cleanedText
                                                                    End If
                                                                End If
                                                            End Using
                                                        Next
                                                    End Using
                                                End Using

                                                ' 如果跑完所有角度都沒命中期望字串，就回傳這幀裡分數最高的結果
                                                Return New With {.Text = localBestText, .Score = localBestScore, .IsMatched = False}
                                            End Function)

                ' ─── 外層即時早停與狀態處理 ───
                ' 【核心修正 3】如果內部回報已命中期望文本，無視分數，立刻終結 While 迴圈直接返回！
                If result.IsMatched Then
                    Return result.Text
                End If

                ' 保底與無期望文本時的處理
                If Not String.IsNullOrWhiteSpace(result.Text) Then
                    If result.Score > bestScore Then
                        bestScore = result.Score
                        bestText = result.Text
                    End If

                    ' 無期望文本時的高置信度提前中斷
                    If expectedTexts.Count = 0 AndAlso result.Score >= 0.8 Then
                        Logger.Info($"[FLOW] OCR 高置信度識別成功 (無期望文本): {result.Text} (Score={result.Score:F3})")
                        Return result.Text
                    End If
                End If
            Else
                If frameCount Mod 10 = 0 Then
                    Logger.Warn($"[OCR] 等待相機畫面中... CamId={cameraId}")
                End If
            End If

            ' 由於內部運算已經從數百毫秒縮減到幾毫秒，這裡的 Delay 可以保持或縮短，確保實時性
            Await Task.Delay(50)
        End While

        ' 超時後的歷史最高分保底返回
        If Not String.IsNullOrWhiteSpace(bestText) Then
            Logger.Info($"[FLOW] OCR 結束，未完全匹配期望文本，返回歷史最高分結果: {bestText} (Score={bestScore:F3})")
            Return bestText
        End If

        Logger.Warn("[FLOW] OCR 超時，未識別到任何有效文本")
        Return ""
    End Function
End Class
