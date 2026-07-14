Imports Cv = OpenCvSharp
Imports System.Linq
Imports System.Threading.Tasks ' 補上 As Task 異步宣告所需的命名空間，防止找不到 Task 的編譯錯誤

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

    Private Shared Function EnhanceForMatching(src As Cv.Mat, config As TemplateConfig) As Cv.Mat
        Dim work As New Cv.Mat()

        If src.Channels() > 1 Then
            Cv.Cv2.CvtColor(src, work, Cv.ColorConversionCodes.BGR2GRAY)
        Else
            work = src.Clone()
        End If

        Dim clipLimit As Double = If(config.CannyLow > 0, Math.Min(config.CannyLow / 40.0, 8.0), 2.0)
        Using clahe = Cv.Cv2.CreateCLAHE(clipLimit, New Cv.Size(8, 8)) ' 明確指定 Cv.Size
            Dim enhanced As New Cv.Mat()
            clahe.Apply(work, enhanced)
            work.Dispose()
            work = enhanced
        End Using

        Dim level = Math.Max(0, Math.Min(config.PyramidLevel, 3))
        For i = 1 To level
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

        ' ROI 限制搜尋
        Dim searchRect As Cv.Rect ' 明確指定 Cv.Rect
        Dim hasRoi = (config.RoiW > 0 AndAlso config.RoiH > 0 AndAlso
                      config.RoiX >= 0 AndAlso config.RoiY >= 0)
        'ROI範圍
        If hasRoi Then
            Dim marginX = CInt(config.RoiW * 0.85)
            Dim marginY = CInt(config.RoiH * 0.85)
            Dim sx = Math.Max(0, config.RoiX - marginX)
            Dim sy = Math.Max(0, config.RoiY - marginY)
            Dim sw = Math.Min(input.Width - sx, config.RoiW + marginX * 2)
            Dim sh = Math.Min(input.Height - sy, config.RoiH + marginY * 2)

            If sw > templateMat.Width AndAlso sh > templateMat.Height Then
                searchRect = New Cv.Rect(sx, sy, sw, sh) ' 明確指定 Cv.Rect
                Logger.Debug($"[MATCH] ROI 限制搜尋: ({sx},{sy}) {sw}x{sh} (原圖 {input.Width}x{input.Height})")
            Else
                hasRoi = False
            End If
        End If

        Dim searchSrc As Cv.Mat = If(hasRoi, New Cv.Mat(input, searchRect), input)

        ' 動態增強：CLAHE + 金字塔
        Dim srcWork = EnhanceForMatching(searchSrc, config)
        Dim tplWork = EnhanceForMatching(templateMat, config)

        Dim result = Await TemplateMatcher.MatchAsync(
            srcWork,
            tplWork,
            config.Threshold,
            config.MatchMethod)

        srcWork.Dispose()
        tplWork.Dispose()
        If hasRoi Then searchSrc.Dispose()

        ' 還原金字塔降採樣帶來的坐標縮放
        Dim scale As Integer = CInt(Math.Pow(2, Math.Max(0, Math.Min(config.PyramidLevel, 3))))
        Dim scaledMatchX = result.MatchPoint.X * scale
        Dim scaledMatchY = result.MatchPoint.Y * scale

        ' 將匹配點轉回原圖絕對坐標（加上 ROI 偏移量）
        Dim absMatchX = scaledMatchX + If(hasRoi, searchRect.X, 0)
        Dim absMatchY = scaledMatchY + If(hasRoi, searchRect.Y, 0)

        Dim display = input.Clone()
        Dim isMatchOk As Boolean = result.IsOk AndAlso (result.Score >= config.Threshold)

        If isMatchOk Then
            ' 建立原圖尺寸上的真實 Bounding Box（明確指定 Cv.Rect）
            Dim rect As New Cv.Rect(absMatchX, absMatchY, templateMat.Width, templateMat.Height)

            ' 防止 rect 超出大圖邊界（明確指定 Cv.Rect）
            rect = New Cv.Rect(
                Math.Max(0, Math.Min(rect.X, input.Width - 1)),
                Math.Max(0, Math.Min(rect.Y, input.Height - 1)),
                Math.Min(rect.Width, input.Width - rect.X),
                Math.Min(rect.Height, input.Height - rect.Y))

            ' 在正確的位置繪製綠色包圍框（明確指定 Cv.Scalar）
            Cv.Cv2.Rectangle(display, rect, Cv.Scalar.Lime, 3)

            Using roi As New Cv.Mat(input, rect),
                  gray As New Cv.Mat(),
                  edges As New Cv.Mat()

                Cv.Cv2.CvtColor(roi, gray, Cv.ColorConversionCodes.BGR2GRAY)
                Cv.Cv2.Canny(gray, edges, 80, 160)

                Dim contours As Cv.Point()() = Nothing ' 明確指定 Cv.Point
                Dim hierarchy As Cv.HierarchyIndex() = Nothing
                Cv.Cv2.FindContours(edges, contours, hierarchy,
                    Cv.RetrievalModes.External, Cv.ContourApproximationModes.ApproxSimple)

                ' 修正點：將局部輪廓坐標加上 rect.X/Y 偏移量，明確指定 Cv.Point 與 Cv.Scalar 避免衝突
                If contours IsNot Nothing AndAlso contours.Length > 0 Then
                    Dim offsetContours = contours.Select(Function(c) c.Select(Function(p) New Cv.Point(p.X + rect.X, p.Y + rect.Y)).ToArray()).ToArray()
                    Cv.Cv2.DrawContours(display, offsetContours.Select(Function(c) c.AsEnumerable()), -1, Cv.Scalar.Lime, 2)
                End If
            End Using
        End If

        ' 若使用 ROI 限制，標示搜尋區域（明確指定 Cv.Scalar）
        If hasRoi Then
            Cv.Cv2.Rectangle(display, searchRect, Cv.Scalar.Orange, 1)
        End If

        ' 顯示結果分數與狀態（明確指定 Cv.Scalar 與 Cv.Point）
        Dim textColor = If(isMatchOk, Cv.Scalar.Yellow, Cv.Scalar.Red)
        Cv.Cv2.PutText(display, $"Score: {result.Score:F3} ({If(isMatchOk, "OK", "NG")})", New Cv.Point(30, 60),
            Cv.HersheyFonts.HersheySimplex, 1.5, textColor, 3)

        Return New ResultPack With {
            .Mat = display,
            .Score = result.Score,
            .IsOk = isMatchOk
        }
    End Function
End Class