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

    Private Async Sub BtnLoadImage_Click(
    sender As Object,
    e As RoutedEventArgs)

        Try

            Dim path = DialogHelper.OpenImage()
            If String.IsNullOrWhiteSpace(path) Then Return

            Dim mat = Cv.Cv2.ImRead(path)
            _currentMat = mat

            Dim templatePath = LastTemplateStore.Load()
            If String.IsNullOrWhiteSpace(templatePath) Then Return

            Dim templateName = IO.Path.GetFileName(templatePath)

            Dim result = Await Draw_opencv.ProcessAsync(mat, templateName)

            RenderImage.Source =
            result.Mat.ToWriteableBitmap()

            Logger.Info($"Score={result.Score:F3}, OK={result.IsOk}")

            If _io IsNot Nothing Then
                Dim snapshot = TemplateSnapshotStore.Load()
                If snapshot IsNot Nothing Then
                    _io.TriggerByScore(result.Score, snapshot.Threshold)
                End If
            End If

        Catch ex As Exception
            ErrorDialogHelper.ShowError("ROI錯誤: " & ex.Message)
        End Try

    End Sub
#Region "相機觸發"
    Private Sub PlayPromptVoice(fileName As String)
        If String.IsNullOrWhiteSpace(fileName) Then Return
        If _io Is Nothing Then
            Logger.Warn($"[VOICE] IOController尚未初始化，無法播報: {fileName}")
            Return
        End If
        Dim voicePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Voice", fileName)
        If Not File.Exists(voicePath) Then
            Logger.Warn($"[VOICE] 語音檔案不存在: {voicePath}")
            Return
        End If
        Try
            Logger.Debug($"[VOICE] 播報: {fileName}")
            _io.PlayCustomVoice(fileName)
        Catch ex As Exception
            Logger.Error($"[VOICE] 播報失敗: {ex.Message}")
        End Try
    End Sub
    Private Async Sub BtnGetImg_Click_Handler(sender As Object, e As RoutedEventArgs) Handles BtnGetImg.Click

        Try
            Logger.Info("[UI] 即時檢測按鈕 - 按下")

            ' 如果検測正在進行中，按下按鈕會跳過當前階段
            If IsSkippableStageRunning() Then
                SyncLock _detectLock
                    Logger.Info($"[UI] 跳過當前阶段: {_flowStage}")
                    _skipCurrentStageRequested = True
                End SyncLock
                Return
            End If

            Logger.Info("[UI] 即時檢測 - 開始")

            Dim result = Await RunDetectionOnce()

            If result Is Nothing Then
                Logger.Error("[UI] 即時檢測 - 失敗")
                Return
            End If

            If Not result.IsFinal Then
                Logger.Info($"[UI] 即時檢測流程進行中：{result.Stage}")
                Return
            End If

            ' ⭐ 保存結果到 ProcessPage，供 Client 任務使用
            If AppRuntime.Process IsNot Nothing Then
                AppRuntime.Process.SetDetectionResult(result)
                Logger.Info("[UI] 即時檢測結果已發送至 ProcessPage")
            End If

        Catch ex As Exception
            Logger.Error($"[UI] 即時檢測錯誤: {ex.Message}")
        End Try

    End Sub

#End Region
    ' =========================================
    ' IO 按鈕處理
    ' =========================================

    Private Async Sub IoButtonChanged(state As Boolean)

        Try
            If Not state Then Return ' 只在按下時觸發

            Logger.Info("[IO] 物理按鈕按下")

            ' 如果検測正在進行中，按下按鈕會跳過當前階段
            If IsSkippableStageRunning() Then
                SyncLock _detectLock
                    Logger.Info($"[IO] 跳過當前阶段: {_flowStage}")
                    _skipCurrentStageRequested = True
                End SyncLock
                Return
            End If

            Logger.Info("[IO] 物理按鈕 - 啟動檢測")

            Dim result = Await RunDetectionOnce()

            If result Is Nothing Then
                Logger.Error("[IO] 即時檢測失敗或無影像")
                Return
            End If

            If Not result.IsFinal Then
                Logger.Info($"[IO] 即時檢測流程進行中：{result.Stage}")
                Return
            End If

            If AppRuntime.Process IsNot Nothing Then
                AppRuntime.Process.SetDetectionResult(result)
                Logger.Info("[IO] 即時檢測結果已發送至 ProcessPage")
            End If

        Catch ex As Exception
            Logger.Error("IoButtonChanged error: " & ex.Message)
        End Try

    End Sub

    ' =========================================
    ' Clear
    ' =========================================
    Private Sub BtnClear_Click(
        sender As Object,
        e As RoutedEventArgs)

        Try

            _currentMat = Nothing

            RenderImage.Source = Nothing

            rtbLog.Document.Blocks.Clear()

            Logger.Info("已清空")

        Catch ex As Exception

            MessageBox.Show(ex.Message)

        End Try

    End Sub
#Region "清晰度評估暨OCR"
    Private Async Sub BtnLaplacian_Click(
    sender As Object,
    e As RoutedEventArgs)

        Try

            Dim bestFrame As BitmapSource = Nothing
            Dim bestScore As Double = Double.MinValue

            Dim sb As New StringBuilder()
            Dim sw As New Stopwatch()
            sw.Start()

            ' 1. Laplacian 選最佳幀
            While sw.ElapsedMilliseconds < 3000

                Dim frame = CameraService.Instance.GetFrame(_ocrCameraId)

                If frame IsNot Nothing Then

                    Dim score = Laplacian.GetScore(frame)

                    sb.AppendLine($"Score={score:F2}")

                    If score > bestScore Then
                        bestScore = score
                        bestFrame = frame.Clone()
                    End If

                End If

                Await Task.Delay(33)

            End While

            Logger.Debug("===== Laplacian Result =====")
            Logger.Debug(sb.ToString())
            Logger.Debug($"Best laplacian Score={bestScore:F2}")

            If bestFrame Is Nothing Then Return

            RenderImage.Source = bestFrame

            Dim bestMat =
            BitmapSourceConverter.ToMat(bestFrame)

            Dim roi As New OpenCvSharp.Rect(
            0,
            0,
            bestMat.Width,
            bestMat.Height)

            Dim ocrResult = Await Task.Run(Function()

                                               Dim angles() As Double =
    {
        -15, -10, -5,
        0,
        5, 10, 15
    }

                                               Dim bestText As String = ""
                                               Dim bestOcrScore As Double = 0
                                               Dim bestAngle As Double = 0

                                               For Each angle In angles

                                                   Using rotated = RotateMat(bestMat, angle)

                                                       Dim result = _ocr.RunRoi(rotated, roi)

                                                       If result IsNot Nothing Then

                                                           Logger.Debug(
                    $"Angle={angle} Text={result.Text} Score={result.Score:F3}")

                                                           If result.Score > bestOcrScore Then

                                                               bestOcrScore = result.Score
                                                               bestText = result.Text
                                                               bestAngle = angle

                                                           End If

                                                           If result.Score >= 0.9 Then
                                                               Exit For
                                                           End If

                                                       End If

                                                   End Using

                                               Next

                                               ' 沒有 0.9 就用最高分
                                               Return New With {
        .Text = bestText,
        .Score = bestOcrScore,
        .Angle = bestAngle
    }

                                           End Function)

            Logger.Debug("===== OCR Result =====")
            Logger.Debug($"Text={ocrResult.Text}")
            Logger.Debug($"Score={ocrResult.Score:F3}")
            Logger.Debug($"Angle={ocrResult.Angle}")

            MessageBox.Show(
            $"OCR結果：{ocrResult.Text}" &
            vbCrLf &
            $"置信度：{ocrResult.Score:F3}" &
            vbCrLf &
            $"最佳角度：{ocrResult.Angle}")

        Catch ex As Exception

            ErrorDialogHelper.ShowError(
                "清晰度評估失敗：" &
                vbCrLf &
                ex.Message)
        End Try
    End Sub
    Private Function RotateMat(
    src As Mat,
    angle As Double) As Mat

        Dim center As New Point2f(
        src.Width / 2.0F,
        src.Height / 2.0F)

        Dim matrix =
        Cv2.GetRotationMatrix2D(
            center,
            angle,
            1.0)

        Dim dst As New Mat()

        Cv2.WarpAffine(
        src,
        dst,
        matrix,
        src.Size(),
        InterpolationFlags.Linear,
        BorderTypes.Constant,
        Scalar.White)

        Return dst

    End Function
#End Region

    Private Async Sub BtnOcrTest_Click(sender As Object, e As RoutedEventArgs)

        Try

            Dim timeoutMs As Integer = 5000
            Dim sw As New Stopwatch()
            sw.Start()

            Dim bestText As String = ""
            Dim bestScore As Double = 0
            Dim bestAngle As Double = 0

            'Dim angles() As Double = {-135， -90, -45, -15, 0, 15, 45, 90, 135}
            Dim angles() As Double = {-45, 0, 15, 45}

            While sw.ElapsedMilliseconds < timeoutMs

                Dim ocrCamId = GetCamId(1)
                If String.IsNullOrEmpty(ocrCamId) Then Return
                Dim frame As BitmapSource = CameraService.Instance.GetFrame(ocrCamId)

                If frame IsNot Nothing Then

                    RenderImage.Dispatcher.Invoke(Sub()
                                                      RenderImage.Source = frame
                                                  End Sub)

                    Dim mat = BitmapSourceConverter.ToMat(frame)
                    Dim roi As New OpenCvSharp.Rect(0, 0, mat.Width, mat.Height)

                    Dim result = Await Task.Run(Function()

                                                    Dim localBestText As String = ""
                                                    Dim localBestScore As Double = 0
                                                    Dim localBestAngle As Double = 0

                                                    For Each angle In angles

                                                        Using rotated = RotateMat(mat, angle)

                                                            Dim ocr = _ocr.RunRoi(rotated, roi)

                                                            If ocr IsNot Nothing Then

                                                                Logger.Debug(
                                                                 $"Angle={angle} Text={ocr.Text} Score={ocr.Score:F3}")

                                                                If ocr.Score > localBestScore Then
                                                                    localBestScore = ocr.Score
                                                                    localBestText = ocr.Text
                                                                    localBestAngle = angle
                                                                End If

                                                                If ocr.Score >= 0.8 Then
                                                                    Exit For
                                                                End If

                                                            End If

                                                        End Using

                                                    Next

                                                    Return New With {
                                                     .Text = localBestText,
                                                     .Score = localBestScore,
                                                     .Angle = localBestAngle
                                                 }

                                                End Function)

                    If result.Score > bestScore Then
                        bestScore = result.Score
                        bestText = result.Text
                        bestAngle = result.Angle
                    End If

                    If bestScore >= 0.8 Then Exit While

                End If

                Await Task.Delay(300)

            End While

            Logger.Debug("===== FINAL =====")
            Logger.Debug($"Text={bestText}")
            Logger.Debug($"Score={bestScore:F3}")
            Logger.Debug($"Angle={bestAngle}")

            MessageBox.Show(
            $"OCR結果：{bestText}" & vbCrLf &
            $"置信度：{bestScore:F3}" & vbCrLf &
            $"角度：{bestAngle}")

        Catch ex As Exception
            ErrorDialogHelper.ShowError("OCR失敗：" & ex.Message)
        End Try

    End Sub
End Class
