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

    ' 多次匹配方法：在3秒内每200ms嘗試一次，收集最高分，最後比對閾值決定OK/NG
    ' 多次匹配方法：在3秒内每200ms嘗試一次，收集最高分，最後比對閾值決定OK/NG
    Private Async Function WaitMultipleMatchAsync(templatePath As String, snapshot As TemplateSnapshot, timeoutMs As Integer) As Task(Of MatchResultWrapper)
        Dim sw As New Stopwatch()
        sw.Start()

        ' 直接從路徑加載母版
        Dim masterData = TemplateManager.LoadTemplate(templatePath)
        If masterData Is Nothing OrElse masterData.Template Is Nothing Then
            Logger.Warn($"[FLOW] 無法加載母版模板: {templatePath}")
            Return New MatchResultWrapper With {.Result = Nothing}
        End If

        Dim cameraId = ResolveMatchCameraId()

        ' 如果相機已在運行且有畫面，直接使用；否則重啟
        If CameraService.Instance.GetFrame(cameraId) Is Nothing Then
            CameraService.Instance.StopAll()
            CameraService.Instance.StartCamera(cameraId)
            Dim warmupSw As New Stopwatch()
            warmupSw.Start()
            While CameraService.Instance.GetFrame(cameraId) Is Nothing AndAlso warmupSw.ElapsedMilliseconds < 2000
                Await Task.Delay(50)
            End While
            Logger.Debug($"[MATCH] 相機沖熱完成 ({warmupSw.ElapsedMilliseconds}ms)")
        Else
            Logger.Debug("[MATCH] 相機已有畫面，跳過沖熱")
        End If

        Dim bestResultMat As Cv.Mat = Nothing
        Dim bestScore As Double = 0
        Dim bestThreshold As Double = masterData.Config.Threshold
        Dim matchAttempt As Integer = 0
        Dim lastAttemptTime As Long = -200

        Dim groupPath = IO.Path.GetDirectoryName(templatePath)
        Dim subTemplateMetas = TemplateTrainingStore.GetTrainingSamples(groupPath)
        Logger.Debug($"[MATCH] 子模板數量={If(subTemplateMetas IsNot Nothing, subTemplateMetas.Count, 0)}, groupPath={groupPath}")

        Const AttemptIntervalMs As Long = 200

        While sw.ElapsedMilliseconds < timeoutMs
            If sw.ElapsedMilliseconds - lastAttemptTime >= AttemptIntervalMs Then
                matchAttempt += 1
                lastAttemptTime = sw.ElapsedMilliseconds

                Dim frame As BitmapSource = Nothing
                If Not String.IsNullOrWhiteSpace(cameraId) Then
                    frame = CameraService.Instance.GetFrame(cameraId)
                End If

                If frame IsNot Nothing Then
                    Dim frameCopy = frame
                    Dispatcher.Invoke(Sub() RenderImage.Source = frameCopy)

                    Using currentMat = BitmapSourceToMat(frame)

                        ' =================================================================
                        ' 【核心修復 1】計算並擷取 ROI 局部影像來做質量檢驗
                        ' 避免高解析度相機的全圖空白背景稀釋了密封圈的特徵密度
                        ' =================================================================
                        Dim roiRect = ResolveRoi(snapshot, currentMat)

                        Dim isFrameValid As Boolean = False

                        Using roiMat = currentMat.SubMat(roiRect)
                            Using gray = roiMat.CvtColor(ColorConversionCodes.BGR2GRAY)
                                Dim meanVal = Cv2.Mean(gray).Val0

                                ' 1. 檢查亮度（在 ROI 內）
                                If meanVal < 15 OrElse meanVal > 245 Then
                                    Logger.Warn($"[MATCH] 跳過異常亮度幀(ROI): mean={meanVal:F1}")
                                    GoTo SkipFrame ' 使用 GoTo 安全跳出並維持 Using 釋放
                                End If

                                ' 2. 檢查方差/對比度（在 ROI 內）
                                Dim meanScalar As New Cv.Scalar()
                                Dim stdDevScalar As New Cv.Scalar()
                                Cv2.MeanStdDev(gray, meanScalar, stdDevScalar)
                                Dim stdDev = stdDevScalar.Val0

                                If stdDev < 10 Then
                                    Logger.Warn($"[MATCH] 跳過對比度過低的幀(ROI): stdDev={stdDev:F1}")
                                    GoTo SkipFrame
                                End If

                                ' 3. 檢查邊緣密度（在 ROI 內）
                                Dim edgeDensity As Double = 0
                                Using edges As New Cv.Mat()
                                    Cv2.Canny(gray, edges, 80, 160)
                                    Dim edgeCount = Cv2.CountNonZero(edges)
                                    Dim roiPixels = edges.Width * edges.Height
                                    ' 計算密度
                                    edgeDensity = CDbl(edgeCount) / CDbl(roiPixels)

                                    ' 如果是高解析度全圖，此值很容易小於 0.01；但在局部 ROI 內可以維持 0.005 ~ 0.01
                                    If edgeDensity < 0.005 Then
                                        Logger.Warn($"[MATCH] 跳過邊緣密度過低的幀(ROI): density={edgeDensity:P2} ({edgeCount}/{roiPixels})")
                                        GoTo SkipFrame
                                    End If
                                End Using

                                Logger.Debug($"[MATCH] #{matchAttempt} 幀質量檢驗通過(ROI): mean={meanVal:F1}, stdDev={stdDev:F1}, edgeDensity={edgeDensity:P2}")
                                isFrameValid = True
                            End Using
                        End Using

SkipFrame:
                        ' 如果檢驗沒通過，直接進入下一次 Delay 循環
                        If Not isFrameValid Then Continue While

                        ' =================================================================
                        ' 【核心修復 2】只有質量檢驗通過，才開始進行耗時的 OpenCV 匹配與存檔準備
                        ' =================================================================

                        ' 嘗試匹配母版
                        Dim masterResult = Await Draw_opencv.ProcessAsync(currentMat, masterData.Template, masterData.Config)
                        If masterResult IsNot Nothing AndAlso masterResult.Score > bestScore Then
                            bestScore = masterResult.Score
                            bestThreshold = masterData.Config.Threshold

                            ' 安全釋放舊的最高分 Mat，避免記憶體洩漏
                            If bestResultMat IsNot Nothing Then bestResultMat.Dispose()
                            bestResultMat = masterResult.Mat?.Clone() ' 必須使用 Clone，否則會隨著 Using 被釋放

                            Logger.Debug($"[MATCH] #{matchAttempt} 母版 Score={masterResult.Score:F3} (閾值={masterData.Config.Threshold:F3})")
                            If masterResult.Mat IsNot Nothing Then
                                Dim wb = masterResult.Mat.ToWriteableBitmap()
                                Dispatcher.Invoke(Sub() RenderImage.Source = wb)
                            End If
                        End If

                        ' 嘗試匹配所有子模板
                        If subTemplateMetas IsNot Nothing AndAlso subTemplateMetas.Count > 0 Then
                            For Each subMeta In subTemplateMetas
                                Dim subMat = TemplateTrainingStore.LoadTrainingSampleImage(groupPath, subMeta.FileName)
                                If subMat IsNot Nothing Then
                                    Try
                                        Dim subConfig As New TemplateConfig With {
                                            .Threshold = If(subMeta.MasterThreshold > 0, subMeta.MasterThreshold, masterData.Config.Threshold),
                                            .MatchMethod = subMeta.MatchMethod,
                                            .PyramidLevel = subMeta.PyramidLevel,
                                            .CannyLow = If(subMeta.CannyLow > 0, subMeta.CannyLow, masterData.Config.CannyLow),
                                            .CannyHigh = If(subMeta.CannyHigh > 0, subMeta.CannyHigh, masterData.Config.CannyHigh)
                                        }
                                        Dim subResult = Await Draw_opencv.ProcessAsync(currentMat, subMat, subConfig)
                                        If subResult IsNot Nothing AndAlso subResult.Score > bestScore Then
                                            bestScore = subResult.Score
                                            bestThreshold = subConfig.Threshold

                                            If bestResultMat IsNot Nothing Then bestResultMat.Dispose()
                                            bestResultMat = subResult.Mat?.Clone()

                                            Logger.Debug($"[MATCH] #{matchAttempt} 子模板'{subMeta.FileName}' Score={subResult.Score:F3} (閾值={subConfig.Threshold:F3})")
                                            If subResult.Mat IsNot Nothing Then
                                                Dim wb = subResult.Mat.ToWriteableBitmap()
                                                Dispatcher.Invoke(Sub() RenderImage.Source = wb)
                                            End If
                                        End If
                                    Finally
                                        subMat.Dispose()
                                    End Try
                                End If
                            Next
                        End If
                    End Using
                Else
                    Logger.Warn($"[MATCH] #{matchAttempt}: 無法獲取相機畫面（請檢查生產線觸發信號或曝光時間）")
                End If
            End If

            Await Task.Delay(50)
        End While

        Dim isOk = (bestScore >= bestThreshold)
        Logger.Info($"[MATCH] 3秒結束，最高分={bestScore:F3}, 對應閾值={bestThreshold:F3}, IsOk={isOk}, 共{matchAttempt}次")

        Dim finalResult As New Draw_opencv.ResultPack With {
            .Score = bestScore,
            .IsOk = isOk,
            .Mat = bestResultMat ' 這邊帶有最高分的視覺影像將能順利傳回外層進行存檔
        }
        CameraService.Instance.StopCamera(cameraId)
        Return New MatchResultWrapper With {.Result = finalResult, .MatchCount = matchAttempt}
    End Function
End Class