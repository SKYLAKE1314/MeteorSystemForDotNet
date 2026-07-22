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
    methodIndex As Integer,
    Optional scaleTolerance As Double = 0.0) As MatchResult
        Return MatchCore(
            source,
            template,
            threshold,
            methodIndex,
            scaleTolerance)

    End Function

    ' =========================================
    ' 異步匹配

    Public Shared Async Function MatchAsync(
    source As Mat,
    template As Mat,
    threshold As Double,
    methodIndex As Integer,
    Optional scaleTolerance As Double = 0.0) As Task(Of MatchResult)

        ' 如果傳入的物件已經不合法，直接返回
        If source Is Nothing OrElse source.IsDisposed OrElse template Is Nothing OrElse template.IsDisposed Then
            Return Nothing
        End If

        ' 這樣能確保傳進背景執行緒的 Mat 擁有獨立生命週期，絕不會被外部 UI 循環提前銷毀
        Dim srcCopyForThread As Mat = source.Clone()
        Dim tplCopyForThread As Mat = template.Clone()

        Return Await Task.Run(
            Function()
                ' 背景執行緒專用 Using，確保不論成功失敗，此安全副本都會被乾淨釋放
                Using srcCopy = srcCopyForThread,
                      tplCopy = tplCopyForThread

                    Return MatchCore(
                        srcCopy,
                        tplCopy,
                        threshold,
                        methodIndex,
                        scaleTolerance)
                End Using
            End Function)

    End Function
    Private Shared Function MatchCore(
    source As Mat,
    template As Mat,
    threshold As Double,
    methodIndex As Integer,
    scaleTolerance As Double) As MatchResult

        Dim mode As TemplateMatchModes = IndexToMode(methodIndex)

        Using graySrc As New Mat(), grayTpl As New Mat()

            ' 1. 強制轉換為單通道灰階圖
            If source.Channels() > 1 Then
                Cv2.CvtColor(source, graySrc, ColorConversionCodes.BGR2GRAY)
            Else
                source.CopyTo(graySrc)
            End If

            If template.Channels() > 1 Then
                Cv2.CvtColor(template, grayTpl, ColorConversionCodes.BGR2GRAY)
            Else
                template.CopyTo(grayTpl)
            End If

            ' 1.5 (移除 CLAHE) CLAHE 是局部自適應演算法，將其套用於切割好的小面積子模板與大面積搜尋圖
            ' 會因為周邊背景資訊不同，導致兩邊拉伸出的對比度完全對不上。
            ' 這會使得產品只要稍微移動一點點（背景稍微改變），匹配分數就會暴跌！

            ' 計算搜尋大圖的平均值與標準差。標準差 (StdDev) 代表圖像紋理的複雜度。
            ' 如果大圖是純黑、純白、純色或幾乎沒紋理的雜訊，標準差會極低（通常 < 8）。
            Dim meanSrc As Scalar = Nothing
            Dim stdDevSrc As Scalar = Nothing
            Cv2.MeanStdDev(graySrc, meanSrc, stdDevSrc)

            ' 如果影像幾乎沒有紋理，直接判定不匹配，強制將分數歸零，徹底封殺 OpenCV 數學幻覺！
            If stdDevSrc.Val0 < 5.0 Then
                Logger.Debug($"[MATCH] 拒絕匹配：搜尋區域影像標準差過低 ({stdDevSrc.Val0:F2})，判定為無紋理純色畫面。")
                Return New MatchResult With {
                    .Score = 0,
                    .MatchPoint = New Point(0, 0),
                    .IsOk = False,
                    .ResultImage = source.Clone()
                }
            End If

            ' 執行匹配運算
            ' --- 支援多尺度 (Scale Tolerance) ---
            Dim bestOverallScore As Double = 0
            Dim bestOverallMatchPoint As Point
            Dim bestScale As Double = 1.0

            Dim scaleStep As Double = 0.05
            Dim minScale As Double = 1.0 - scaleTolerance
            Dim maxScale As Double = 1.0 + scaleTolerance
            If minScale < 0.1 Then minScale = 0.1

            For scale As Double = minScale To maxScale + 0.001 Step scaleStep
                Using scaledTpl As New Mat()
                    If Math.Abs(scale - 1.0) < 0.01 Then
                        grayTpl.CopyTo(scaledTpl)
                    Else
                        Dim newSize As New OpenCvSharp.Size(Math.Max(1, CInt(grayTpl.Width * scale)), Math.Max(1, CInt(grayTpl.Height * scale)))
                        If newSize.Width > graySrc.Width OrElse newSize.Height > graySrc.Height Then
                            Continue For
                        End If
                        Cv2.Resize(grayTpl, scaledTpl, newSize)
                    End If

                    ' --- 正常極性匹配 ---
                    Dim score As Double = 0
                    Dim matchPoint As Point
                    TrySingleMatch(graySrc, scaledTpl, mode, score, matchPoint)

                    ' --- 極性反轉匹配 分支 ---
                    If mode <> TemplateMatchModes.SqDiffNormed AndAlso score < 0.5 Then
                        Using invTpl As New Mat()
                            Cv2.BitwiseNot(scaledTpl, invTpl)
                            Dim score2 As Double = 0
                            Dim matchPoint2 As Point
                            TrySingleMatch(graySrc, invTpl, mode, score2, matchPoint2)
                            If score2 > score Then
                                score = score2
                                matchPoint = matchPoint2
                            End If
                        End Using
                    End If

                    If score > bestOverallScore Then
                        bestOverallScore = score
                        bestOverallMatchPoint = matchPoint
                        bestScale = scale
                    End If
                End Using
            Next
            
            Dim finalScore As Double = bestOverallScore
            Dim finalMatchPoint As Point = bestOverallMatchPoint
            Dim finalWidth As Integer = Math.Max(1, CInt(template.Width * bestScale))
            Dim finalHeight As Integer = Math.Max(1, CInt(template.Height * bestScale))

            ' 2. 建立返回結果
            Dim display As Mat = source.Clone()

            ' 如果算出來的高分依然大於閾值，但此時分數恰好是踩中 CCorrNormed 的純色陷阱
            ' 我們做二次安全檢查：確保匹配到的區域 (ROI)，其標準差不能跟模板天壤地別
            Dim ok As Boolean = finalScore >= threshold

            If ok Then
                ' 裁切出大圖上被匹配到的那一塊區域
                Dim matchedRect As New Rect(finalMatchPoint.X, finalMatchPoint.Y, finalWidth, finalHeight)

                ' 防止邊界溢出安全保護
                If matchedRect.X >= 0 AndAlso matchedRect.Y >= 0 AndAlso
                   (matchedRect.X + matchedRect.Width) <= graySrc.Width AndAlso
                   (matchedRect.Y + matchedRect.Height) <= graySrc.Height Then

                    Using matchedRoi As New Mat(graySrc, matchedRect)
                        Dim mMean As Scalar = Nothing, mStd As Scalar = Nothing
                        Cv2.MeanStdDev(matchedRoi, mMean, mStd)

                        ' 如果匹配到的地方其實是死黑一片或完全沒特徵，強行拉倒
                        If mStd.Val0 < 3.0 Then
                            ok = False
                            finalScore = 0
                            Logger.Debug($"[MATCH] 判定攔截：匹配區域標準差過低 ({mStd.Val0:F2})，此為虛假高分點。")
                        Else
                            ' 已移除直方圖校驗，因為訓練時模板的背景會被塗抹，導致直方圖完全不同而錯誤地壓低分數
                        End If
                    End Using
                End If
            End If

            ' 真正過關才畫綠框
            If ok Then
                Cv2.Rectangle(
                    display,
                    New Rect(finalMatchPoint.X, finalMatchPoint.Y, finalWidth, finalHeight),
                    Scalar.Lime,
                    3)
            End If

            Return New MatchResult With {
                .Score = finalScore,
                .MatchPoint = finalMatchPoint,
                .IsOk = ok,
                .ResultImage = display,
                .MatchedWidth = finalWidth,
                .MatchedHeight = finalHeight
            }
        End Using
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

            ' 【修正】已移除邊界假陽性檢驗 (isBoundaryHit)，
            ' 因為此檢驗會導致產品只要稍微移動到畫面邊緣（或是模板與畫面大小接近時），
            ' 匹配分數就會被強制歸 0，造成嚴重的誤殺（稍微移動一點就匹配不上）。
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