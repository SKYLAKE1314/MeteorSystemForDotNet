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

        Dim result = Await TemplateMatcher.MatchAsync(
            input,
            data.Template,
            data.Config.Threshold,
            data.Config.MatchMethod)

        Dim display = input.Clone()

        If result.IsOk Then

            Dim rect As New Cv.Rect(
                result.MatchPoint.X,
                result.MatchPoint.Y,
                data.Template.Width,
                data.Template.Height)

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
    .IsOk = (result.Score >= data.Config.Threshold)
}

    End Function

End Class