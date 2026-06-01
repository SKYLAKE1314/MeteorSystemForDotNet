Imports OpenCvSharp
Imports System.Threading.Tasks

Public Class TemplateMatcher

    ' =========================================
    ' 生成模板
    ' =========================================
    Public Shared Function CreateTemplate(
        source As Mat,
        roi As Rect,
        ByRef preview As Mat) As Mat

        Dim roiMat As New Mat(source, roi)

        Dim gray As New Mat()

        Cv2.CvtColor(
            roiMat,
            gray,
            ColorConversionCodes.BGR2GRAY)

        Dim edges As New Mat()

        Cv2.Canny(
            gray,
            edges,
            80,
            160)

        ' 避免 Nothing
        Dim contours As Point()() = {}
        Dim hierarchy As HierarchyIndex() = {}

        Cv2.FindContours(
            edges,
            contours,
            hierarchy,
            RetrievalModes.External,
            ContourApproximationModes.ApproxSimple)

        preview = roiMat.Clone()

        Cv2.DrawContours(
            preview,
            contours,
            -1,
            Scalar.Lime,
            2)

        Return roiMat.Clone()

    End Function

    ' =========================================
    ' 同步匹配
    ' =========================================
    Public Shared Function Match(
        source As Mat,
        template As Mat,
        threshold As Double,
        methodIndex As Integer) As MatchResult

        Return MatchCore(
            source,
            template,
            threshold,
            methodIndex)

    End Function

    ' =========================================
    ' 異步匹配
    ' =========================================
    Public Shared Async Function MatchAsync(
        source As Mat,
        template As Mat,
        threshold As Double,
        methodIndex As Integer) As Task(Of MatchResult)

        Return Await Task.Run(
            Function()

                ' 避免跨執行緒Mat問題
                Using srcCopy = source.Clone(),
                      tplCopy = template.Clone()

                    Return MatchCore(
                        srcCopy,
                        tplCopy,
                        threshold,
                        methodIndex)

                End Using

            End Function)

    End Function

    ' =========================================
    ' Match Core
    ' =========================================
    Private Shared Function MatchCore(
        source As Mat,
        template As Mat,
        threshold As Double,
        methodIndex As Integer) As MatchResult

        Dim result As New Mat()

        Dim mode As TemplateMatchModes =
            TemplateMatchModes.CCoeffNormed

        Select Case methodIndex

            Case 0
                mode = TemplateMatchModes.CCoeffNormed

            Case 1
                mode = TemplateMatchModes.CCorrNormed

            Case 2
                mode = TemplateMatchModes.SqDiffNormed

        End Select

        Cv2.MatchTemplate(
            source,
            template,
            result,
            mode)

        Dim minVal As Double
        Dim maxVal As Double

        Dim minLoc As Point
        Dim maxLoc As Point

        Cv2.MinMaxLoc(
            result,
            minVal,
            maxVal,
            minLoc,
            maxLoc)

        Dim score As Double
        Dim matchPoint As Point

        If mode = TemplateMatchModes.SqDiffNormed Then

            score = 1.0 - minVal
            matchPoint = minLoc

        Else

            score = maxVal
            matchPoint = maxLoc

        End If

        Dim display As Mat =
            source.Clone()

        Dim ok As Boolean =
            score >= threshold

        If ok Then

            Cv2.Rectangle(
                display,
                New Rect(
                    matchPoint.X,
                    matchPoint.Y,
                    template.Width,
                    template.Height),
                Scalar.Lime,
                3)

        End If

        Return New MatchResult With {
            .Score = score,
            .MatchPoint = matchPoint,
            .IsOk = ok,
            .ResultImage = display
        }

    End Function

End Class