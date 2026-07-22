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

    Private Async Function WaitMultipleMatchAsync(templatePath As String, snapshot As TemplateSnapshot, timeoutMs As Integer) As Task(Of MatchResultWrapper)
        Dim sw As New Stopwatch()
        sw.Start()

        Dim masterData = TemplateManager.LoadTemplate(templatePath)
        If masterData Is Nothing OrElse masterData.Template Is Nothing Then
            Logger.Warn($"[FLOW] 無法加載母版模板: {templatePath}")
            Return New MatchResultWrapper With {.Result = Nothing}
        End If

        Dim cameraId = ResolveMatchCameraId()

        ' 相機預熱
        Dim needsWarmup = (CameraService.Instance.GetFrame(cameraId) Is Nothing)
        If needsWarmup Then
            CameraService.Instance.StartCamera(cameraId)
            Dim warmupSw As New Stopwatch()
            warmupSw.Start()
            While CameraService.Instance.GetFrame(cameraId) Is Nothing AndAlso warmupSw.ElapsedMilliseconds < 3000
                Await Task.Delay(50)
            End While
        End If

        Dim bestResultMat As Cv.Mat = Nothing
        Dim bestScore As Double = 0
        Dim bestThreshold As Double = masterData.Config.Threshold
        Dim matchAttempt As Integer = 0
        Dim lastAttemptTime As Long = -200

        Dim groupPath = IO.Path.GetDirectoryName(templatePath)
        Logger.Info($"[MATCH] templatePath={templatePath}")
        Logger.Info($"[MATCH] groupPath(GetDirectoryName)={groupPath}")
        Logger.Info($"[MATCH] NormalizeGroupPath={TemplateTrainingStore.NormalizeGroupPath(groupPath)}")
        Dim subTemplateMetas = TemplateTrainingStore.GetTrainingSamples(groupPath)

        Dim loadedSubTemplates As New List(Of Tuple(Of Object, Cv.Mat))()
        If subTemplateMetas IsNot Nothing Then
            For Each subMeta In subTemplateMetas
                Dim patchMat = TemplateTrainingStore.LoadTrainingSampleImage(groupPath, subMeta.FileName)
                If patchMat IsNot Nothing Then
                    loadedSubTemplates.Add(Tuple.Create(DirectCast(subMeta, Object), patchMat))
                End If
            Next
        End If
        Logger.Info($"[MATCH] 子模板載入完成，就緒數量={loadedSubTemplates.Count}")

        Const AttemptIntervalMs As Long = 200
        Dim earlyOkResult As Draw_opencv.ResultPack = Nothing

        Try
            While sw.ElapsedMilliseconds < timeoutMs AndAlso earlyOkResult Is Nothing
                If _isPaused Then
                    Await Task.Delay(100)
                    Continue While
                End If

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
                            Dim meanVal As Double = 0, stdDev As Double = 0, edgeDensity As Double = 0

                            Dim cannyL As Double = If(masterData.Config IsNot Nothing AndAlso masterData.Config.CannyLow > 0, CDbl(masterData.Config.CannyLow), 80.0)
                            Dim cannyH As Double = If(masterData.Config IsNot Nothing AndAlso masterData.Config.CannyHigh > 0, CDbl(masterData.Config.CannyHigh), 160.0)

                            Using roiMat = currentMat.SubMat(roiRect)
                                If Not ValidateFrameQuality(roiMat, meanVal, stdDev, edgeDensity, cannyL, cannyH) Then
                                    Logger.Warn($"[MATCH] #{matchAttempt} 影格品質不理想(stdDev={stdDev:F1})，強制交由演算法進行比對。")
                                End If
                            End Using

                            ' ── 1. 嘗試母版 ───────────────────────────────────────────
                            Dim masterResult = Await Draw_opencv.ProcessAsync(currentMat, masterData.Template, masterData.Config)
                            If masterResult IsNot Nothing Then
                                If masterResult.Score > bestScore Then
                                    bestScore = masterResult.Score
                                    bestThreshold = masterData.Config.Threshold

                                    If bestResultMat IsNot Nothing Then bestResultMat.Dispose()
                                    bestResultMat = masterResult.Mat?.Clone()

                                    If masterResult.Mat IsNot Nothing Then
                                        Dim wb = masterResult.Mat.ToWriteableBitmap()
                                        Dispatcher.Invoke(Sub() RenderImage.Source = wb)
                                    End If
                                End If

                                If masterResult.IsOk Then
                                    Logger.Info($"[MATCH] 母版 OK（提前退出）Score={masterResult.Score:F3}")
                                    earlyOkResult = New Draw_opencv.ResultPack With {
                                        .Score = masterResult.Score, .IsOk = True,
                                        .Mat = If(bestResultMat IsNot Nothing, bestResultMat, masterResult.Mat?.Clone())
                                    }
                                    masterResult.Mat?.Dispose()
                                    Exit While
                                End If
                                masterResult.Mat?.Dispose()
                            End If

                            If earlyOkResult IsNot Nothing Then Exit While

                            ' ── 2. 嘗試子模板 ───────────────────────────────────────────
                            For Each item In loadedSubTemplates
                                Dim subMeta = item.Item1
                                Dim subMatPatch = item.Item2

                                Dim subConfig As New TemplateConfig With {
                                    .Threshold = If(subMeta.MasterThreshold > 0, subMeta.MasterThreshold, masterData.Config.Threshold),
                                    .MatchMethod = subMeta.MatchMethod,
                                    .PyramidLevel = subMeta.PyramidLevel,
                                    .CannyLow = If(subMeta.CannyLow > 0, subMeta.CannyLow, masterData.Config.CannyLow),
                                    .CannyHigh = If(subMeta.CannyHigh > 0, subMeta.CannyHigh, masterData.Config.CannyHigh),
                                    .RoiX = masterData.Config.RoiX, .RoiY = masterData.Config.RoiY,
                                    .RoiW = masterData.Config.RoiW, .RoiH = masterData.Config.RoiH
                                }

                                Dim subResult = Await Draw_opencv.ProcessAsync(currentMat, subMatPatch, subConfig)
                                If subResult IsNot Nothing Then
                                    If subResult.Score > bestScore Then
                                        bestScore = subResult.Score
                                        bestThreshold = subConfig.Threshold
                                        If bestResultMat IsNot Nothing Then bestResultMat.Dispose()
                                        bestResultMat = subResult.Mat?.Clone()
                                        Logger.Debug($"[MATCH] #{matchAttempt} 命中子模板'{subMeta.FileName}' Score={subResult.Score:F3}")

                                        If subResult.Mat IsNot Nothing Then
                                            Dim wb = subResult.Mat.ToWriteableBitmap()
                                            Dispatcher.Invoke(Sub() RenderImage.Source = wb)
                                        End If
                                    End If

                                    If subResult.IsOk Then
                                        Logger.Info($"[MATCH] 子模板'{subMeta.FileName}' OK（提前退出）Score={subResult.Score:F3}")
                                        earlyOkResult = New Draw_opencv.ResultPack With {
                                            .Score = subResult.Score, .IsOk = True,
                                            .Mat = If(bestResultMat IsNot Nothing, bestResultMat, subResult.Mat?.Clone())
                                        }
                                        subResult.Mat?.Dispose()
                                        Exit For
                                    End If
                                    subResult.Mat?.Dispose()
                                End If
                            Next
                        End Using
                    End If
                End If

                Await Task.Delay(50)
            End While

        Finally
            For Each item In loadedSubTemplates
                item.Item2.Dispose()
            Next
            loadedSubTemplates.Clear()
        End Try

        If earlyOkResult IsNot Nothing Then
            Return New MatchResultWrapper With {.Result = earlyOkResult, .MatchCount = matchAttempt}
        End If

        Dim isOk = (bestScore >= bestThreshold)
        Logger.Info($"[MATCH] 流程結束，最終最高分={bestScore:F3}, 門檻={bestThreshold:F3}, 結果={isOk}")
        Dim finalResult As New Draw_opencv.ResultPack With {.Score = bestScore, .IsOk = isOk, .Mat = bestResultMat}
        Return New MatchResultWrapper With {.Result = finalResult, .MatchCount = matchAttempt}
    End Function
    Private Function ValidateFrameQuality(
    roiMat As Cv.Mat,
    ByRef outMean As Double,
    ByRef outStdDev As Double,
    ByRef outEdgeDensity As Double,
    Optional cannyLow As Double = 80,
    Optional cannyHigh As Double = 160,
    Optional minEdgeDensity As Double = 0.0005,
    Optional minStdDev As Double = 5.0
) As Boolean

        ' 如果輸入的 ROI 矩陣為空，直接攔截，避免後續 CvtColor 拋出 OpenCV 底層崩潰異常
        If roiMat Is Nothing OrElse roiMat.Empty() Then
            Logger.Warn("[MATCH] 影像驗證失敗：輸入的 ROI Mat 為空或未初始化。")
            Return False
        End If

        Try
            ' 確保 gray 在結束時會被正確釋放釋放 Unmanaged 記憶體
            Using gray As New Cv.Mat()
                ' 轉換為灰階
                Cv2.CvtColor(roiMat, gray, ColorConversionCodes.BGR2GRAY)

                ' ── 1. 檢查亮度與對比度 (標準差) ──
                Dim meanScalar As New Cv.Scalar()
                Dim stdDevScalar As New Cv.Scalar()
                Cv2.MeanStdDev(gray, meanScalar, stdDevScalar)

                outMean = meanScalar.Val0
                outStdDev = stdDevScalar.Val0

                If outStdDev < minStdDev Then
                    Logger.Warn($"[MATCH] 跳過對比度過低的幀(ROI): stdDev={outStdDev:F1} (門檻值={minStdDev})")
                    Return False
                End If

                ' ── 2. 檢查邊緣密度 ──
                Using edges As New Cv.Mat()
                    ' 使用傳入的 Canny 參數
                    Cv2.Canny(gray, edges, cannyLow, cannyHigh)

                    Dim edgeCount As Integer = Cv2.CountNonZero(edges)
                    Dim roiPixels As Long = gray.Total() ' 

                    outEdgeDensity = CDbl(edgeCount) / CDbl(roiPixels)

                    ' 邊緣過濾
                    If outEdgeDensity < minEdgeDensity Then
                        Logger.Warn($"[MATCH] 跳過邊緣密度過低的幀(ROI): density={outEdgeDensity:P3} ({edgeCount}/{roiPixels}, 門檻值={minEdgeDensity:P3})")
                        Return False
                    End If
                End Using
            End Using

            Return True

        Catch ex As Exception
            ' 捕捉未知的 OpenCV 或系統異常，防止整個檢測執行緒中斷
            Logger.Error($"[MATCH] ValidateFrameQuality 執行時發生異常: {ex.Message}")
            Return False
        End Try
    End Function
End Class