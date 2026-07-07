Imports Cv = OpenCvSharp
Imports System.Linq

Public Class Draw_opencv

    Public Class ResultPack
        Public Property Mat As Cv.Mat
        Public Property Score As Double
        Public Property IsOk As Boolean
    End Class

    ' =========================================
    ' 一步完成：匹配 + 畫框 + contour + 分數
    ' =========================================
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

    ' =========================================
    ' 根據 config 參數對圖像進行增強（CLAHE + 金字塔）
    ' =========================================
    Private Shared Function EnhanceForMatching(src As Cv.Mat, config As TemplateConfig) As Cv.Mat
        Dim work As New Cv.Mat()

        ' 統一轉灰度
        If src.Channels() > 1 Then
            Cv.Cv2.CvtColor(src, work, Cv.ColorConversionCodes.BGR2GRAY)
        Else
            work = src.Clone()
        End If

        ' CLAHE 自適應對比增強（CannyLow/High 值越大越強調邊緣，借用做強度參數）
        Dim clipLimit As Double = If(config.CannyLow > 0, Math.Min(config.CannyLow / 40.0, 8.0), 2.0)
        Using clahe = Cv.Cv2.CreateCLAHE(clipLimit, New Cv.Size(8, 8))
            Dim enhanced As New Cv.Mat()
            clahe.Apply(work, enhanced)
            work.Dispose()
            work = enhanced
        End Using

        ' 金字塔降採樣（減少噪點，提高匹配穩定性）
        Dim level = Math.Max(0, Math.Min(config.PyramidLevel, 3))
        For i = 1 To level
            Dim down As New Cv.Mat()
            Cv.Cv2.PyrDown(work, down)
            work.Dispose()
            work = down
        Next

        Return work
    End Function

    ' =========================================
    ' 重載：直接使用已加載的模板 Mat 和配置（繞過 TemplateCache）
    ' 若 config 含有效 ROI，則只在 ROI 區域（含容差）搜尋，避免全幀虛假高分
    ' =========================================
    Public Shared Async Function ProcessAsync(
        input As Cv.Mat,
        templateMat As Cv.Mat,
        config As TemplateConfig) As Task(Of ResultPack)

        If input Is Nothing OrElse templateMat Is Nothing OrElse config Is Nothing Then Return Nothing

        ' ── ROI 限制搜尋 ──────────────────────────────────────
        ' 若模板建立時記錄了 ROI，在 ROI 範圍（±30% margin）內搜尋
        Dim searchRect As Cv.Rect
        Dim hasRoi = (config.RoiW > 0 AndAlso config.RoiH > 0 AndAlso
                      config.RoiX >= 0 AndAlso config.RoiY >= 0)
        If hasRoi Then
            Dim marginX = CInt(config.RoiW * 0.35)
            Dim marginY = CInt(config.RoiH * 0.35)
            Dim sx = Math.Max(0, config.RoiX - marginX)
            Dim sy = Math.Max(0, config.RoiY - marginY)
            Dim sw = Math.Min(input.Width - sx, config.RoiW + marginX * 2)
            Dim sh = Math.Min(input.Height - sy, config.RoiH + marginY * 2)
            ' 搜尋區域至少要比模板大才有意義
            If sw > templateMat.Width AndAlso sh > templateMat.Height Then
                searchRect = New Cv.Rect(sx, sy, sw, sh)
                Logger.Debug($"[MATCH] ROI 限制搜尋: ({sx},{sy}) {sw}x{sh} (原圖 {input.Width}x{input.Height})")
            Else
                hasRoi = False ' fallback 到全幀
            End If
        End If

        ' 截取搜尋區域（或用全幀）
        Dim searchSrc As Cv.Mat = If(hasRoi, New Cv.Mat(input, searchRect), input)

        ' 動態增強：對 source 和 template 套用相同的 CLAHE + 金字塔
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

        ' 將匹配點轉回原圖坐標
        Dim absMatchX = result.MatchPoint.X + If(hasRoi, searchRect.X, 0)
        Dim absMatchY = result.MatchPoint.Y + If(hasRoi, searchRect.Y, 0)

        Dim display = input.Clone()

        If result.IsOk Then
            Dim rect As New Cv.Rect(absMatchX, absMatchY, templateMat.Width, templateMat.Height)

            ' 防止 rect 超出邊界
            rect = New Cv.Rect(
                Math.Max(0, Math.Min(rect.X, input.Width - 1)),
                Math.Max(0, Math.Min(rect.Y, input.Height - 1)),
                Math.Min(rect.Width, input.Width - rect.X),
                Math.Min(rect.Height, input.Height - rect.Y))

            Cv.Cv2.Rectangle(display, rect, Cv.Scalar.Lime, 3)

            Dim roi As New Cv.Mat(input, rect)
            Dim gray As New Cv.Mat()
            Cv.Cv2.CvtColor(roi, gray, Cv.ColorConversionCodes.BGR2GRAY)
            Dim edges As New Cv.Mat()
            Cv.Cv2.Canny(gray, edges, 80, 160)
            Dim contours As Cv.Point()() = Nothing
            Dim hierarchy As Cv.HierarchyIndex() = Nothing
            Cv.Cv2.FindContours(edges, contours, hierarchy,
                Cv.RetrievalModes.External, Cv.ContourApproximationModes.ApproxSimple)
            If contours IsNot Nothing Then
                Cv.Cv2.DrawContours(display(rect), contours.Select(Function(c) c.AsEnumerable()), -1, Cv.Scalar.Lime, 2)
            End If
        End If

        ' 若使用 ROI 限制，在畫面上也標示搜尋區域（橙色虛框方便 debug）
        If hasRoi Then
            Cv.Cv2.Rectangle(display, searchRect, Cv.Scalar.Orange, 1)
        End If

        Cv.Cv2.PutText(display, $"Score: {result.Score:F3}", New Cv.Point(20, 40),
            Cv.HersheyFonts.HersheySimplex, 1.0, Cv.Scalar.Yellow, 2)

        Return New ResultPack With {
            .Mat = display,
            .Score = result.Score,
            .IsOk = (result.Score >= config.Threshold)
        }

    End Function

End Class