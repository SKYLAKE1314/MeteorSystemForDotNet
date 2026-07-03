Imports OpenCvSharp
Imports System.Threading.Tasks

Public Class TemplateMatcher

    ''' <summary>
    ''' 邊界填充寬度（像素），用於軟化 ROI 裁切邊界
    ''' </summary>
    Private Const BOUNDARY_PADDING As Integer = 4

    ' =========================================
    ' 生成模板
    ' =========================================
    Public Shared Function CreateTemplate(
    source As Mat,
    roi As Rect,
    options As TemplateMatchOptions,
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
    options.CannyLow,
    options.CannyHigh)

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
        ' 過濾 小；輪廓
        For Each contour In contours

            Dim area =
        Cv2.ContourArea(contour)

            If area < options.MinContourArea Then
                Continue For
            End If

            Cv2.DrawContours(
        preview,
        {contour},
        -1,
        Scalar.Lime,
        2)

        Next

        ' 返回軟化邊界的模板
        Return SoftenTemplateBoundary(roiMat)

    End Function

    ''' <summary>
    ''' 對模板邊界進行軟化處理，防止邊界被識別為特徵
    ''' 通過在邊界添加漸變過渡來消除清晰的邊緣
    ''' </summary>
    Private Shared Function SoftenTemplateBoundary(template As Mat) As Mat
        If template Is Nothing OrElse template.Empty() Then
            Return template.Clone()
        End If

        Dim result = template.Clone()
        Dim padSize = BOUNDARY_PADDING

        ' 邊界軟化策略：對邊界像素進行漸變填充
        ' 左邊界
        For x As Integer = 0 To Math.Min(padSize - 1, result.Width - 1)
            Dim alpha = CDbl(x) / CDbl(padSize)
            For y As Integer = 0 To result.Height - 1
                Dim pixel = result.At(Of Vec3b)(y, x)
                ' 與暗色背景混合
                pixel.Item0 = CByte(pixel.Item0 * alpha + 32 * (1 - alpha))
                pixel.Item1 = CByte(pixel.Item1 * alpha + 32 * (1 - alpha))
                pixel.Item2 = CByte(pixel.Item2 * alpha + 32 * (1 - alpha))
                result.Set(Of Vec3b)(y, x, pixel)
            Next
        Next

        ' 右邊界
        For x As Integer = Math.Max(0, result.Width - padSize) To result.Width - 1
            Dim alpha = CDbl(result.Width - 1 - x) / CDbl(padSize)
            For y As Integer = 0 To result.Height - 1
                Dim pixel = result.At(Of Vec3b)(y, x)
                pixel.Item0 = CByte(pixel.Item0 * alpha + 32 * (1 - alpha))
                pixel.Item1 = CByte(pixel.Item1 * alpha + 32 * (1 - alpha))
                pixel.Item2 = CByte(pixel.Item2 * alpha + 32 * (1 - alpha))
                result.Set(Of Vec3b)(y, x, pixel)
            Next
        Next

        ' 上邊界
        For y As Integer = 0 To Math.Min(padSize - 1, result.Height - 1)
            Dim alpha = CDbl(y) / CDbl(padSize)
            For x As Integer = 0 To result.Width - 1
                Dim pixel = result.At(Of Vec3b)(y, x)
                pixel.Item0 = CByte(pixel.Item0 * alpha + 32 * (1 - alpha))
                pixel.Item1 = CByte(pixel.Item1 * alpha + 32 * (1 - alpha))
                pixel.Item2 = CByte(pixel.Item2 * alpha + 32 * (1 - alpha))
                result.Set(Of Vec3b)(y, x, pixel)
            Next
        Next

        ' 下邊界
        For y As Integer = Math.Max(0, result.Height - padSize) To result.Height - 1
            Dim alpha = CDbl(result.Height - 1 - y) / CDbl(padSize)
            For x As Integer = 0 To result.Width - 1
                Dim pixel = result.At(Of Vec3b)(y, x)
                pixel.Item0 = CByte(pixel.Item0 * alpha + 32 * (1 - alpha))
                pixel.Item1 = CByte(pixel.Item1 * alpha + 32 * (1 - alpha))
                pixel.Item2 = CByte(pixel.Item2 * alpha + 32 * (1 - alpha))
                result.Set(Of Vec3b)(y, x, pixel)
            Next
        Next

        ' 輕微高斯模糊以進一步軟化邊界
        Dim blurred As New Mat()
        Cv2.GaussianBlur(result, blurred, New Size(3, 3), 0.5)

        ' 只對邊界區域應用模糊
        Dim mask As New Mat()
        mask = Mat.Zeros(result.Size(), MatType.CV_8UC1)

        ' 標記邊界區域
        Cv2.Rectangle(mask, New Rect(0, 0, result.Width, padSize), Scalar.White, -1)
        Cv2.Rectangle(mask, New Rect(0, result.Height - padSize, result.Width, padSize), Scalar.White, -1)
        Cv2.Rectangle(mask, New Rect(0, 0, padSize, result.Height), Scalar.White, -1)
        Cv2.Rectangle(mask, New Rect(result.Width - padSize, 0, padSize, result.Height), Scalar.White, -1)

        blurred.CopyTo(result, mask)
        blurred.Dispose()
        mask.Dispose()

        Return result
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

        Dim srcWork = source.Clone()
        Dim tplWork = template.Clone()

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
    srcWork,
    tplWork,
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