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

    ' 多次匹配方法：在3秒內每200ms嘗試一次，收集最高分，最後比對閾值決定OK/NG
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

        If CameraService.Instance.GetFrame(cameraId) Is Nothing Then
            CameraService.Instance.StartCamera(cameraId)
            Dim warmupSw As New Stopwatch()
            warmupSw.Start()
            While CameraService.Instance.GetFrame(cameraId) Is Nothing AndAlso warmupSw.ElapsedMilliseconds < 2000
                Await Task.Delay(50)
            End While
            Logger.Debug($"[MATCH] 相機沖熱完成 ({warmupSw.ElapsedMilliseconds}ms)")
        Else
            Logger.Debug("[MATCH] 相機已有畫面，繼續使用實時串流")
        End If

        Dim bestResultMat As Cv.Mat = Nothing
        Dim bestScore As Double = 0
        Dim bestThreshold As Double = masterData.Config.Threshold
        Dim matchAttempt As Integer = 0
        Dim lastAttemptTime As Long = -200

        Dim groupPath = IO.Path.GetDirectoryName(templatePath)
        Dim subTemplateMetas = TemplateTrainingStore.GetTrainingSamples(groupPath)
        Logger.Debug($"[MATCH] 子模板數量={If(subTemplateMetas IsNot Nothing, subTemplateMetas.Count, 0)}, groupPath={groupPath}")

        ' =================================================================
        ' 【核心修正 1】將子模板讀取移出 While 迴圈，提前載入記憶體快取
        ' =================================================================
        Dim loadedSubTemplates As New List(Of Tuple(Of Object, Cv.Mat))() ' 註：Object 可替換為您的 subMeta 實際型別
        If subTemplateMetas IsNot Nothing Then
            For Each subMeta In subTemplateMetas
                Dim subMat = TemplateTrainingStore.LoadTrainingSampleImage(groupPath, subMeta.FileName)
                If subMat IsNot Nothing Then
                    loadedSubTemplates.Add(Tuple.Create(DirectCast(subMeta, Object), subMat))
                End If
            Next
        End If

        Const AttemptIntervalMs As Long = 200

        Try
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
                            Dim roiRect = ResolveRoi(snapshot, currentMat)
                            Dim isFrameValid As Boolean = False

                            Dim meanVal As Double = 0
                            Dim stdDev As Double = 0
                            Dim edgeDensity As Double = 0

                            ' 計算並擷取 ROI 局部影像來做質量檢驗
                            Using roiMat = currentMat.SubMat(roiRect)
                                ' 【核心修正 3】呼叫抽離後的檢驗函式，不使用 GoTo
                                isFrameValid = ValidateFrameQuality(roiMat, meanVal, stdDev, edgeDensity)
                            End Using

                            ' 檢驗沒通過，直接進入下一次 Delay 循環
                            If Not isFrameValid Then Continue While

                            Logger.Debug($"[MATCH] #{matchAttempt} 幀質量檢驗通過(ROI): mean={meanVal:F1}, stdDev={stdDev:F1}, edgeDensity={edgeDensity:P2}")

                            ' 嘗試匹配母版
                            Dim masterResult = Await Draw_opencv.ProcessAsync(currentMat, masterData.Template, masterData.Config)
                            If masterResult IsNot Nothing Then
                                If masterResult.Score > bestScore Then
                                    bestScore = masterResult.Score
                                    bestThreshold = masterData.Config.Threshold

                                    ' 安全釋放舊的最高分 Mat
                                    If bestResultMat IsNot Nothing Then bestResultMat.Dispose()
                                    bestResultMat = masterResult.Mat?.Clone()

                                    Logger.Debug($"[MATCH] #{matchAttempt} 母版 Score={masterResult.Score:F3} (閾值={masterData.Config.Threshold:F3})")
                                    If masterResult.Mat IsNot Nothing Then
                                        Dim wb = masterResult.Mat.ToWriteableBitmap()
                                        Dispatcher.Invoke(Sub() RenderImage.Source = wb)
                                    End If
                                End If

                                ' 【核心修正 2】及時釋放 ProcessAsync 產生的臨時影像，避免記憶體洩漏
                                masterResult.Mat?.Dispose()
                            End If

                            ' 嘗試匹配所有子模板（此處直接從記憶體快取讀取，速度極快）
                            For Each item In loadedSubTemplates
                                Dim subMeta = item.Item1 ' 若上面替換了型別，此處可直接使用
                                Dim subMat = item.Item2

                                Dim subConfig As New TemplateConfig With {
                                .Threshold = If(subMeta.MasterThreshold > 0, subMeta.MasterThreshold, masterData.Config.Threshold),
                                .MatchMethod = subMeta.MatchMethod,
                                .PyramidLevel = subMeta.PyramidLevel,
                                .CannyLow = If(subMeta.CannyLow > 0, subMeta.CannyLow, masterData.Config.CannyLow),
                                .CannyHigh = If(subMeta.CannyHigh > 0, subMeta.CannyHigh, masterData.Config.CannyHigh)
                            }

                                Dim subResult = Await Draw_opencv.ProcessAsync(currentMat, subMat, subConfig)
                                If subResult IsNot Nothing Then
                                    If subResult.Score > bestScore Then
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

                                    ' 【核心修正 2】及時釋放子模板臨時影像
                                    subResult.Mat?.Dispose()
                                End If
                            Next
                        End Using
                    Else
                        Logger.Warn($"[MATCH] #{matchAttempt}: 無法獲取相機畫面（請檢查生產線觸發信號或曝光時間）")
                    End If
                End If

                Await Task.Delay(50)
            End While

        Finally
            ' =================================================================
            ' 【核心修正 1 續】不論 While 成功或中途出錯，最終統一清空子模板記憶體
            ' =================================================================
            For Each item In loadedSubTemplates
                item.Item2.Dispose()
            Next
            loadedSubTemplates.Clear()
        End Try

        Dim isOk = (bestScore >= bestThreshold)
        Logger.Info($"[MATCH] 3秒結束，最高分={bestScore:F3}, 對應閾值={bestThreshold:F3}, IsOk={isOk}, 共{matchAttempt}次")

        Dim finalResult As New Draw_opencv.ResultPack With {
        .Score = bestScore,
        .IsOk = isOk,
        .Mat = bestResultMat ' 帶有最高分的視覺影像，交由外部呼叫者負責最後的 Dispose
    }
        'CameraService.Instance.StopCamera(cameraId)
        Return New MatchResultWrapper With {.Result = finalResult, .MatchCount = matchAttempt}
    End Function

    Private Function ValidateFrameQuality(roiMat As Cv.Mat, ByRef outMean As Double, ByRef outStdDev As Double, ByRef outEdgeDensity As Double) As Boolean
        Using gray = roiMat.CvtColor(ColorConversionCodes.BGR2GRAY)
            ' 1. 檢查亮度
            outMean = Cv2.Mean(gray).Val0
            If outMean < 15 OrElse outMean > 245 Then
                Logger.Warn($"[MATCH] 跳過異常亮度幀(ROI): mean={outMean:F1}")
                Return False
            End If

            ' 2. 檢查方差/對比度
            Dim meanScalar As New Cv.Scalar()
            Dim stdDevScalar As New Cv.Scalar()
            Cv2.MeanStdDev(gray, meanScalar, stdDevScalar)
            outStdDev = stdDevScalar.Val0
            If outStdDev < 10 Then
                Logger.Warn($"[MATCH] 跳過對比度過低的幀(ROI): stdDev={outStdDev:F1}")
                Return False
            End If

            ' 3. 檢查邊緣密度
            Using edges As New Cv.Mat()
                Cv2.Canny(gray, edges, 80, 160)
                Dim edgeCount = Cv2.CountNonZero(edges)
                Dim roiPixels = edges.Width * edges.Height
                outEdgeDensity = CDbl(edgeCount) / CDbl(roiPixels)
                ' 边缘過濾
                If outEdgeDensity < 0.0015 Then
                    Logger.Warn($"[MATCH] 跳過邊緣密度過低的幀(ROI): density={outEdgeDensity:P2} ({edgeCount}/{roiPixels})")
                    Return False
                End If
            End Using
        End Using

        Return True
    End Function
End Class