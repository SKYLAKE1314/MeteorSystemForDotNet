Imports OpenCvSharp
Public Class ContourRenderer
    Public Shared Function Render(template As Mat) As Mat

        Dim gray As New Mat()

        Cv2.CvtColor(
            template,
            gray,
            ColorConversionCodes.BGR2GRAY)

        Dim blur As New Mat()

        Cv2.GaussianBlur(
            gray,
            blur,
            New Size(5, 5),
            0)

        Dim edge As New Mat()

        Cv2.Canny(
            blur,
            edge,
            80,
            160)

        Dim contours()() As Point
        Dim hierarchy() As HierarchyIndex

        Cv2.FindContours(
            edge,
            contours,
            hierarchy,
            RetrievalModes.External,
            ContourApproximationModes.ApproxSimple)

        Dim preview As Mat =
            template.Clone()

        Cv2.DrawContours(
            preview,
            contours,
            -1,
            Scalar.Lime,
            2)

        Return preview

    End Function
End Class
