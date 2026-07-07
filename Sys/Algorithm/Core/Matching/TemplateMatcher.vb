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
    ''' 對模板邊界進行軟化處理，防止邊界被識別為特徵。
    ''' 邊界淡化目標為模板的平均色（而非固定暗色），
    ''' 避免產生人工的矩形邊框特徵導致虛假高分匹配。
    ''' </summary>
    Private Shared Function SoftenTemplateBoundary(template As Mat) As Mat
        If template Is Nothing OrElse template.Empty() Then
            Return template.Clone()
        End If

        Dim result = template.Clone()
        Dim padSize = BOUNDARY_PADDING

        ' 計算模板各通道平均色，邊界淡化到此顏色而非固定的暗色，
        ' 避免產生矩形邊框特徵（固定暗色 + CLAHE 增強 = 假矩形輪廓）
        Dim meanScalar = Cv2.Mean(result)
        Dim meanB = CByte(Math.Round(meanScalar.Val0))
        Dim meanG = CByte(Math.Round(meanScalar.Val1))
        Dim meanR = CByte(Math.Round(meanScalar.Val2))

        ' 左邊界
        For x As Integer = 0 To Math.Min(padSize - 1, result.Width - 1)
            Dim alpha = CDbl(x) / CDbl(padSize)
            For y As Integer = 0 To result.Height - 1
                Dim pixel = result.At(Of Vec3b)(y, x)
                pixel.Item0 = CByte(pixel.Item0 * alpha + meanB * (1 - alpha))
                pixel.Item1 = CByte(pixel.Item1 * alpha + meanG * (1 - alpha))
                pixel.Item2 = CByte(pixel.Item2 * alpha + meanR * (1 - alpha))
                result.Set(Of Vec3b)(y, x, pixel)
            Next
        Next

        ' 右邊界
        For x As Integer = Math.Max(0, result.Width - padSize) To result.Width - 1
            Dim alpha = CDbl(result.Width - 1 - x) / CDbl(padSize)
            For y As Integer = 0 To result.Height - 1
                Dim pixel = result.At(Of Vec3b)(y, x)
                pixel.Item0 = CByte(pixel.Item0 * alpha + meanB * (1 - alpha))
                pixel.Item1 = CByte(pixel.Item1 * alpha + meanG * (1 - alpha))
                pixel.Item2 = CByte(pixel.Item2 * alpha + meanR * (1 - alpha))
                result.Set(Of Vec3b)(y, x, pixel)
            Next
        Next

        ' 上邊界
        For y As Integer = 0 To Math.Min(padSize - 1, result.Height - 1)
            Dim alpha = CDbl(y) / CDbl(padSize)
            For x As Integer = 0 To result.Width - 1
                Dim pixel = result.At(Of Vec3b)(y, x)
                pixel.Item0 = CByte(pixel.Item0 * alpha + meanB * (1 - alpha))
                pixel.Item1 = CByte(pixel.Item1 * alpha + meanG * (1 - alpha))
                pixel.Item2 = CByte(pixel.Item2 * alpha + meanR * (1 - alpha))
                result.Set(Of Vec3b)(y, x, pixel)
            Next
        Next

        ' 下邊界
        For y As Integer = Math.Max(0, result.Height - padSize) To result.Height - 1
            Dim alpha = CDbl(result.Height - 1 - y) / CDbl(padSize)
            For x As Integer = 0 To result.Width - 1
                Dim pixel = result.At(Of Vec3b)(y, x)
                pixel.Item0 = CByte(pixel.Item0 * alpha + meanB * (1 - alpha))
                pixel.Item1 = CByte(pixel.Item1 * alpha + meanG * (1 - alpha))
                pixel.Item2 = CByte(pixel.Item2 * alpha + meanR * (1 - alpha))
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

        Dim mode As TemplateMatchModes = IndexToMode(methodIndex)

        ' --- Normal polarity match ---
        Dim score As Double
        Dim matchPoint As Point
        TrySingleMatch(source, template, mode, score, matchPoint)

        ' --- Polarity check: if score is poor, try inverted source ---
        ' Handles dark-on-light vs light-on-dark mismatch between template and source.
        ' Skip for SqDiff (inversion semantics differ) and when already a strong match.
        If mode <> TemplateMatchModes.SqDiffNormed AndAlso score < 0.5 Then
            Using invSrc As New Mat()
                Cv2.BitwiseNot(source, invSrc)
                Dim score2 As Double
                Dim matchPoint2 As Point
                TrySingleMatch(invSrc, template, mode, score2, matchPoint2)
                If score2 > score Then
                    Logger.Debug($"[MATCH] 極性反轉改善分數: {score:F3} -> {score2:F3}")
                    score = score2
                    matchPoint = matchPoint2
                End If
            End Using
        End If

        ' Draw result on original source
        Dim display As Mat = source.Clone()
        Dim ok As Boolean = score >= threshold

        If ok Then
            Cv2.Rectangle(
                display,
                New Rect(matchPoint.X, matchPoint.Y, template.Width, template.Height),
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

    ''' <summary>執行單次 MatchTemplate 並返回最佳分數及位置。</summary>
    Private Shared Sub TrySingleMatch(src As Mat, tpl As Mat, mode As TemplateMatchModes,
                                      ByRef score As Double, ByRef matchPt As Point)
        Using result As New Mat()
            Cv2.MatchTemplate(src, tpl, result, mode)
            Dim minVal As Double, maxVal As Double
            Dim minLoc As Point, maxLoc As Point
            Cv2.MinMaxLoc(result, minVal, maxVal, minLoc, maxLoc)
            If mode = TemplateMatchModes.SqDiffNormed Then
                score = 1.0 - minVal
                matchPt = minLoc
            Else
                score = maxVal
                matchPt = maxLoc
            End If

            ' 邊界假陽性檢驗：若匹配點落在結果圖的邊緣（±1px），
            ' 通常是模板邊界特徵與搜尋區邊界的錯誤匹配，降低分數為 0
            Dim resultW = result.Width
            Dim resultH = result.Height
            If resultW > 2 AndAlso resultH > 2 Then
                Dim isBoundaryHit = (matchPt.X <= 1 OrElse matchPt.X >= resultW - 2 OrElse
                                     matchPt.Y <= 1 OrElse matchPt.Y >= resultH - 2)
                If isBoundaryHit AndAlso score > 0.5 Then
                    Logger.Debug($"[MATCH] 邊界假陽性：matchPt=({matchPt.X},{matchPt.Y}) resultSize={resultW}x{resultH}，分數由 {score:F3} 降為 0")
                    score = 0
                End If
            End If
        End Using
    End Sub

    Private Shared Function IndexToMode(idx As Integer) As TemplateMatchModes
        Select Case idx
            Case 1 : Return TemplateMatchModes.CCorrNormed
            Case 2 : Return TemplateMatchModes.SqDiffNormed
            Case Else : Return TemplateMatchModes.CCoeffNormed
        End Select
    End Function

End Class