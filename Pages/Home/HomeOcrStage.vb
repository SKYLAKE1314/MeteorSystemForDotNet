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

        ' 使用當前設定中的 OCR 相機（或回退到檢測相機）
        Dim cameraId = _ocrCameraId
        If String.IsNullOrWhiteSpace(cameraId) Then
            cameraId = _matchCameraId
        End If
        If String.IsNullOrWhiteSpace(cameraId) Then
            cameraId = GetCamId(0)  ' 回退到第一個相機
        End If

        If String.IsNullOrWhiteSpace(cameraId) Then
            Logger.Error("[OCR] 無法取得有效的 OCR 相機 ID")
            Return ""
        End If

        ' =================================================================
        ' 【重要修復】補上進入點日誌，保證流程切換時一定看得見
        ' =================================================================
        Logger.Info($"[FLOW] === 進入 OCR 階段 === 選定相機 CamId={cameraId}, 超時設定={timeoutMs}ms")

        ' =================================================================
        ' 【核心修復】移除舊有盲目的 StopAll() 與 Thread.Sleep
        ' 避免雙相機架構下硬體斷流、重啟，防止工業相機 SDK 底層資源衝突死鎖
        ' =================================================================
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
        Dim frameCount As Integer = 0

        While sw.ElapsedMilliseconds < timeoutMs
            If IsSkipRequested(DetectionFlowStage.Ocr) Then
                Logger.Info("[FLOW] OCR 已跳過")
                Return Nothing  ' Nothing=跳過 / ""=超時 / 非空=成功
            End If

            Dim frame = CameraService.Instance.GetFrame(cameraId)
            If frame IsNot Nothing Then
                frameCount += 1
                Dim frameCopy = frame

                ' =================================================================
                ' 【優化】改為 BeginInvoke 非同步渲染，防止 UI 執行緒與非同步任務互相等待導致死鎖
                ' =================================================================
                Dispatcher.BeginInvoke(Sub() RenderImage.Source = frameCopy)

                ' 所有OCR運算放入Task.Run，避免UI卡頓
                ' 所有OCR運算放入Task.Run，避免UI卡頓
                Dim result = Await Task.Run(Function()
                                                Dim localBestText As String = ""
                                                Dim localBestScore As Double = 0

                                                Using mat = BitmapSourceToMat(frameCopy)
                                                    Dim roi = ResolveRoi(snapshot, mat)

                                                    ' =================================================================
                                                    ' 【核心修復】自動防錯：若 ROI 縮水得太誇張，自動釋放為全畫面辨識
                                                    ' =================================================================
                                                    If roi.Width < 10 OrElse roi.Height < 10 Then
                                                        Logger.Warn($"[OCR] 檢測到異常微小的 ROI ({roi.X},{roi.Y},{roi.Width}x{roi.Height})，可能越界或未設定！自動切換為【全畫面】辨識。")
                                                        roi = New OpenCvSharp.Rect(0, 0, mat.Width, mat.Height)
                                                    End If

                                                    ' 首次獲取畫面時，Log 記錄當前 OCR 實際使用的 ROI 範圍
                                                    If frameCount = 1 Then
                                                        Logger.Debug($"[OCR] 圖像尺寸={mat.Width}x{mat.Height}, 實際辨識區域 ROI={roi.X},{roi.Y},{roi.Width}x{roi.Height}")
                                                    End If

                                                    For Each angle In angles
                                                        Using rotated = RotateMat(mat, angle)
                                                            Dim ocrResult = ocr.RunRoi(rotated, roi)

                                                            If ocrResult IsNot Nothing AndAlso
                                               Not String.IsNullOrWhiteSpace(ocrResult.Text) Then

                                                                ' 只要有辨識出任何東西（不管對不對），這裡就一定會印日誌！
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
                        Logger.Info($"[FLOW] OCR 高置信度識別成功 (無期望文本): {result.Text} (Score={result.Score:F3})")
                        Return result.Text
                    End If
                End If
            Else
                ' 【新增保底日誌】如果相機重啟中導致暫時拿不到畫面，每隔 10 幀提示一次，避免無日誌盲區
                If frameCount Mod 10 = 0 Then
                    Logger.Warn($"[OCR] 等待相機畫面中... CamId={cameraId} (可能相機串流尚未建立)")
                End If
            End If

            Await Task.Delay(100) ' OCR 帧間小休，防止多角度重複匹配卡點
        End While

        ' 超時後的保底返回
        If Not String.IsNullOrWhiteSpace(bestText) Then
            Logger.Info($"[FLOW] OCR 結束，未完全匹配期望文本，返回歷史最高分結果: {bestText} (Score={bestScore:F3})")
            Return bestText
        End If

        Logger.Warn("[FLOW] OCR 超時，未識別到任何有效文本")
        Return ""
    End Function
End Class
