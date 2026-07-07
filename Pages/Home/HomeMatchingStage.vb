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
    Private Async Function WaitMultipleMatchAsync(templatePath As String, snapshot As TemplateSnapshot, timeoutMs As Integer) As Task(Of MatchResultWrapper)
        Dim sw As New Stopwatch()
        sw.Start()

        ' 直接從路徑加載母版（繞過 TemplateCache，確保加載最新模板）
        Dim masterData = TemplateManager.LoadTemplate(templatePath)
        If masterData Is Nothing OrElse masterData.Template Is Nothing Then
            Logger.Warn($"[FLOW] 無法加載母版模板: {templatePath}")
            Return New MatchResultWrapper With {.Result = Nothing}
        End If

        ' =================================================================
        ' 【核心修復】定位匹配一律調用專屬的 ResolveMatchCameraId()（相機 1）
        ' =================================================================
        Dim cameraId = ResolveMatchCameraId()

        ' 如果相機已在運行且有畫面，直接使用；否則重啟
        If CameraService.Instance.GetFrame(cameraId) Is Nothing Then
            CameraService.Instance.StopAll()
            CameraService.Instance.StartCamera(cameraId)
            ' 等待相機產生第一幀，最多等 2 秒
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
        Dim bestThreshold As Double = masterData.Config.Threshold ' 追蹤最高分對應的閾值
        Dim matchAttempt As Integer = 0
        Dim lastAttemptTime As Long = -200 ' 立即觸發第一次匹配

        ' 獲取子模板列表（groupPath = 母版的父目錄，即 test3 資料夾）
        Dim groupPath = IO.Path.GetDirectoryName(templatePath)
        Dim subTemplateMetas = TemplateTrainingStore.GetTrainingSamples(groupPath)
        Logger.Debug($"[MATCH] 子模板數量={If(subTemplateMetas IsNot Nothing, subTemplateMetas.Count, 0)}, groupPath={groupPath}")

        ' 每200ms嘗試一次（3秒內最多約15次）
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
                    ' 更新預覽畫面
                    Dim frameCopy = frame
                    Dispatcher.Invoke(Sub() RenderImage.Source = frameCopy)

                    Using currentMat = BitmapSourceToMat(frame)
                        Dim gray = currentMat.CvtColor(ColorConversionCodes.BGR2GRAY)
                        Dim meanVal = Cv2.Mean(gray).Val0

                        ' ===== 改進的內容檢驗 =====
                        ' 1. 檢查亮度：過暗或過亮都跳過
                        If meanVal < 15 OrElse meanVal > 245 Then
                            Logger.Warn($"[MATCH] 跳過異常亮度幀: mean={meanVal:F1}")
                            Continue While
                        End If

                        ' 2. 檢查方差：檢測圖像的對比度（區分空白背景）
                        Dim meanScalar As New Cv.Scalar()
                        Dim stdDevScalar As New Cv.Scalar()
                        Cv2.MeanStdDev(gray, meanScalar, stdDevScalar)
                        Dim stdDev = stdDevScalar.Val0

                        ' 方差過低表示圖像過於均勻（如空白背景或單色背景）
                        If stdDev < 10 Then
                            Logger.Warn($"[MATCH] 跳過對比度過低的幀: stdDev={stdDev:F1}")
                            Continue While
                        End If

                        ' 3. 檢查邊緣密度：確保有足夠的邊緣特徵
                        Dim edges As New Cv.Mat()
                        Cv2.Canny(gray, edges, 80, 160)
                        Dim edgeCount = Cv2.CountNonZero(edges)
                        Dim totalPixels = edges.Width * edges.Height
                        Dim edgeDensity = CDbl(edgeCount) / CDbl(totalPixels)

                        ' 邊緣密度過低表示圖像內容不足（< 1% 邊緣為異常）
                        If edgeDensity < 0.01 Then
                            Logger.Warn($"[MATCH] 跳過邊緣密度過低的幀: density={edgeDensity:P2} ({edgeCount}/{totalPixels})")
                            edges.Dispose()
                            Continue While
                        End If

                        edges.Dispose()
                        Logger.Debug($"[MATCH] #{matchAttempt} 幀質量檢驗通過: mean={meanVal:F1}, stdDev={stdDev:F1}, edgeDensity={edgeDensity:P2}")

                        ' 嘗試匹配母版（使用母版自己的 config）
                        Dim masterResult = Await Draw_opencv.ProcessAsync(currentMat, masterData.Template, masterData.Config)
                        If masterResult IsNot Nothing AndAlso masterResult.Score > bestScore Then
                            bestScore = masterResult.Score
                            bestThreshold = masterData.Config.Threshold
                            bestResultMat = masterResult.Mat
                            Logger.Debug($"[MATCH] #{matchAttempt} 母版 Score={masterResult.Score:F3} (閾值={masterData.Config.Threshold:F3})")
                            ' 即時渲染匹配結果（含框線和分數）
                            If masterResult.Mat IsNot Nothing Then
                                Dim wb = masterResult.Mat.ToWriteableBitmap()
                                Dispatcher.Invoke(Sub() RenderImage.Source = wb)
                            End If
                        End If

                        ' 嘗試匹配所有子模板（每個子模板用自己的 params）
                        If subTemplateMetas IsNot Nothing AndAlso subTemplateMetas.Count > 0 Then
                            For Each subMeta In subTemplateMetas
                                Dim subMat = TemplateTrainingStore.LoadTrainingSampleImage(groupPath, subMeta.FileName)
                                If subMat IsNot Nothing Then
                                    Try
                                        ' 從 TrainingSampleMeta 建立此子模板專屬的 TemplateConfig
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
                                            bestResultMat = subResult.Mat
                                            Logger.Debug($"[MATCH] #{matchAttempt} 子模板'{subMeta.FileName}' Score={subResult.Score:F3} (閾值={subConfig.Threshold:F3})")
                                            ' 即時渲染匹配結果
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
                    Logger.Warn($"[MATCH] #{matchAttempt}: 無法獲取相機畫面")
                End If
            End If

            Await Task.Delay(50)
        End While

        ' 3秒結束後，用最高分與對應閾值比較決定 OK/NG
        Dim isOk = (bestScore >= bestThreshold)
        Logger.Info($"[MATCH] 3秒結束，最高分={bestScore:F3}, 對應閾值={bestThreshold:F3}, IsOk={isOk}, 共{matchAttempt}次")

        Dim finalResult As New Draw_opencv.ResultPack With {
            .Score = bestScore,
            .IsOk = isOk,
            .Mat = bestResultMat
        }
        CameraService.Instance.StopCamera(cameraId)
        Return New MatchResultWrapper With {.Result = finalResult, .MatchCount = matchAttempt}
    End Function
End Class