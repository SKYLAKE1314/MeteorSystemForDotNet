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
    ' =========================================
    Public Shared Async Function ProcessAsync(
        input As Cv.Mat,
        templateMat As Cv.Mat,
        config As TemplateConfig) As Task(Of ResultPack)

        If input Is Nothing OrElse templateMat Is Nothing OrElse config Is Nothing Then Return Nothing

        ' 動態增強：對 source 和 template 套用相同的 CLAHE + 金字塔
        Dim srcWork = EnhanceForMatching(input, config)
        Dim tplWork = EnhanceForMatching(templateMat, config)

        Dim result = Await TemplateMatcher.MatchAsync(
            srcWork,
            tplWork,
            config.Threshold,
            config.MatchMethod)

        srcWork.Dispose()
        tplWork.Dispose()

        Dim display = input.Clone()

        If result.IsOk Then

            Dim rect As New Cv.Rect(
                result.MatchPoint.X,
                result.MatchPoint.Y,
                templateMat.Width,
                templateMat.Height)

            ' =========================
            ' Draw box
            ' =========================
            Cv.Cv2.Rectangle(display, rect, Cv.Scalar.Lime, 3)

            ' =========================
            ' ROI contour
            ' =========================
            Dim roi As New Cv.Mat(input, rect)

            Dim gray As New Cv.Mat()
            Cv.Cv2.CvtColor(roi, gray, Cv.ColorConversionCodes.BGR2GRAY)

            Dim edges As New Cv.Mat()
            Cv.Cv2.Canny(gray, edges, 80, 160)

            Dim contours As Cv.Point()() = Nothing
            Dim hierarchy As Cv.HierarchyIndex() = Nothing

            Cv.Cv2.FindContours(
                edges,
                contours,
                hierarchy,
                Cv.RetrievalModes.External,
                Cv.ContourApproximationModes.ApproxSimple)

            If contours IsNot Nothing Then

                Dim contourList =
                    contours.Select(Function(c) c.AsEnumerable())

                Cv.Cv2.DrawContours(
                    display(rect),
                    contourList,
                    -1,
                    Cv.Scalar.Lime,
                    2)

            End If

        End If

        ' =========================
        ' Score text
        ' =========================
        Cv.Cv2.PutText(
            display,
            $"Score: {result.Score:F3}",
            New Cv.Point(20, 40),
            Cv.HersheyFonts.HersheySimplex,
            1.0,
            Cv.Scalar.Yellow,
            2)

        Return New ResultPack With {
            .Mat = display,
            .Score = result.Score,
            .IsOk = (result.Score >= config.Threshold)
        }

    End Function

End Class