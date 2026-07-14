Imports Cv = OpenCvSharp
Imports System.Linq
Imports System.Threading.Tasks
Imports System.Windows.Media

Public Class Draw_opencv

    Public Class ResultPack
        Public Property Mat As Cv.Mat
        Public Property Score As Double
        Public Property IsOk As Boolean
    End Class

    Public Shared Async Function ProcessAsync(
        input As Cv.Mat,
        templateName As String) As Task(Of ResultPack)

        If input Is Nothing Then Return Nothing

        Dim data = TemplateCache.GetTemplate(templateName)
        If data Is Nothing Then
            Return New ResultPack With {
                .Mat = input.Clone(),
                .Score = 0,
                .IsOk = False
            }
        End If

        Return Await ProcessAsync(input, data.Template, data.Config)
    End Function

    ' =================================================================
    ' 【⚡ 絕對對稱防禦版】智慧特徵增強器
    ' =================================================================
    Private Shared Function EnhanceForMatching(src As Cv.Mat, config As TemplateConfig, actualLevel As Integer) As Cv.Mat
        Dim work As New Cv.Mat()

        If src.Channels() > 1 Then
            Cv.Cv2.CvtColor(src, work, Cv.ColorConversionCodes.BGR2GRAY)
        Else
            work = src.Clone()
        End If

        ' 無論大圖還是小模板，都必須套用 CLAHE，保證對比度特徵絕對一致！
        ' 透過動態計算 GridSize (最大8x8)，確保小模板不會因為網格太大而引發 OpenCV 底層崩潰。
        Dim gridW = Math.Max(1, Math.Min(8, work.Width \ 8))
        Dim gridH = Math.Max(1, Math.Min(8, work.Height \ 8))
        Dim clipLimit As Double = If(config.CannyLow > 0, Math.Min(config.CannyLow / 40.0, 8.0), 2.0)

        Using clahe = Cv.Cv2.CreateCLAHE(clipLimit, New Cv.Size(gridW, gridH))
            Dim enhanced As New Cv.Mat()
            clahe.Apply(work, enhanced)
            work.Dispose()
            work = enhanced
        End Using

        ' 強迫雙方使用完全相同的 actualLevel，保證特徵尺寸比例 1:1 完美契合！
        For i = 1 To actualLevel
            Dim down As New Cv.Mat()
            Cv.Cv2.PyrDown(work, down)
            work.Dispose()
            work = down
        Next

        Return work
    End Function

    Public Shared Async Function ProcessAsync(
        input As Cv.Mat,
        templateMat As Cv.Mat,
        config As TemplateConfig) As Task(Of ResultPack)

        If input Is Nothing OrElse templateMat Is Nothing OrElse config Is Nothing Then Return Nothing
        If input.IsDisposed OrElse templateMat.IsDisposed Then Return Nothing
        If input.CvPtr = IntPtr.Zero OrElse templateMat.CvPtr = IntPtr.Zero Then Return Nothing

        Dim tplWidth As Integer = 0
        Dim tplHeight As Integer = 0
        Try
            tplWidth = templateMat.Width
            tplHeight = templateMat.Height
        Catch ex As Exception
            System.Diagnostics.Debug.WriteLine($"[MATCH] 讀取 templateMat 尺寸時遭遇執行緒搶佔: {ex.Message}")
            Return New ResultPack With {.Mat = input.Clone(), .Score = 0, .IsOk = False}
        End Try

        ' 找出能讓「子模板」安全縮小的最大級別 (避免縮到小於 8 像素而崩潰)
        Dim actualLevel As Integer = Math.Max(0, Math.Min(config.PyramidLevel, 3))
        While actualLevel > 0 AndAlso ((tplWidth >> actualLevel) < 8 OrElse (tplHeight >> actualLevel) < 8)
            actualLevel -= 1
        End While

        Dim searchRect As Cv.Rect
        Dim hasRoi As Boolean = False


        Dim localInput As Cv.Mat = input.Clone()
        Dim localTemplate As Cv.Mat = templateMat.Clone()
        Dim searchSrc As Cv.Mat = localInput

        Dim srcWork As Cv.Mat = Nothing
        Dim tplWork As Cv.Mat = Nothing
        Dim display As Cv.Mat = Nothing
        Dim isMatchOk As Boolean = False
        Dim resultScore As Double = 0

        Try
            ' 傳入共同的 actualLevel，保證大圖和小圖受到 100% 絕對對稱的特徵處理！
            srcWork = EnhanceForMatching(searchSrc, config, actualLevel)
            tplWork = EnhanceForMatching(localTemplate, config, actualLevel)

            ' 呼叫非同步匹配
            Dim result = Await TemplateMatcher.MatchAsync(
                srcWork,
                tplWork,
                config.Threshold,
                config.MatchMethod)

            resultScore = result.Score

            ' 透過真實的寬高比，精準還原金字塔降採樣帶來的座標縮放
            Dim actualScaleW As Double = CDbl(searchSrc.Width) / CDbl(srcWork.Width)
            Dim actualScaleH As Double = CDbl(searchSrc.Height) / CDbl(srcWork.Height)

            Dim scaledMatchX = CInt(Math.Round(result.MatchPoint.X * actualScaleW))
            Dim scaledMatchY = CInt(Math.Round(result.MatchPoint.Y * actualScaleH))

            ' 將匹配點轉回原圖絕對坐標（加上 ROI 偏移量）
            Dim absMatchX = scaledMatchX + If(hasRoi, searchRect.X, 0)
            Dim absMatchY = scaledMatchY + If(hasRoi, searchRect.Y, 0)

            display = localInput.Clone()
            isMatchOk = result.IsOk AndAlso (result.Score >= config.Threshold)

            If isMatchOk Then
                Dim rect As New Cv.Rect(absMatchX, absMatchY, tplWidth, tplHeight)

                rect = New Cv.Rect(
                    Math.Max(0, Math.Min(rect.X, display.Width - 1)),
                    Math.Max(0, Math.Min(rect.Y, display.Height - 1)),
                    Math.Min(rect.Width, display.Width - rect.X),
                    Math.Min(rect.Height, display.Height - rect.Y))

                Cv.Cv2.Rectangle(display, rect, Cv.Scalar.Lime, 3)

                Using roi As New Cv.Mat(display, rect),
                      gray As New Cv.Mat(),
                      edges As New Cv.Mat()

                    Cv.Cv2.CvtColor(roi, gray, Cv.ColorConversionCodes.BGR2GRAY)
                    Cv.Cv2.Canny(gray, edges, 80, 160)

                    Dim contours As Cv.Point()() = Nothing
                    Dim hierarchy As Cv.HierarchyIndex() = Nothing
                    Cv.Cv2.FindContours(edges, contours, hierarchy,
                        Cv.RetrievalModes.External, Cv.ContourApproximationModes.ApproxSimple)

                    If contours IsNot Nothing AndAlso contours.Length > 0 Then
                        Dim offsetContours = contours.Select(Function(c) c.Select(Function(p) New Cv.Point(p.X + rect.X, p.Y + rect.Y)).ToArray()).ToArray()
                        Cv.Cv2.DrawContours(display, offsetContours.Select(Function(c) c.AsEnumerable()), -1, Cv.Scalar.Lime, 2)
                    End If
                End Using
            End If

            If hasRoi Then
                Cv.Cv2.Rectangle(display, searchRect, Cv.Scalar.Orange, 1)
            End If

            Dim textColor = If(isMatchOk, Cv.Scalar.Yellow, Cv.Scalar.Red)
            Cv.Cv2.PutText(display, $"Score: {resultScore:F3} ({If(isMatchOk, "OK", "NG")})", New Cv.Point(30, 60),
                Cv.HersheyFonts.HersheySimplex, 1.5, textColor, 3)

            Dim finalPack = New ResultPack With {
                .Mat = display.Clone(),
                .Score = resultScore,
                .IsOk = isMatchOk
            }
            Return finalPack

        Catch ex As Exception
            Logger.Error($"[MATCH] ProcessAsync 執行運算失敗: {ex.Message}")
            Return New ResultPack With {.Mat = input.Clone(), .Score = 0, .IsOk = False}
        Finally
            If srcWork IsNot Nothing AndAlso Not srcWork.IsDisposed Then srcWork.Dispose()
            If tplWork IsNot Nothing AndAlso Not tplWork.IsDisposed Then tplWork.Dispose()
            If display IsNot Nothing AndAlso Not display.IsDisposed Then display.Dispose()
            If hasRoi AndAlso searchSrc IsNot Nothing AndAlso Not searchSrc.IsDisposed Then searchSrc.Dispose()
            If localInput IsNot Nothing AndAlso Not localInput.IsDisposed Then localInput.Dispose()
            If localTemplate IsNot Nothing AndAlso Not localTemplate.IsDisposed Then localTemplate.Dispose()
        End Try
    End Function
End Class