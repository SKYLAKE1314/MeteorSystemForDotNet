Imports OpenCvSharp
Imports OpenCvSharp.WpfExtensions
Imports System.Windows
Imports System.Windows.Input

Imports CvRect = OpenCvSharp.Rect

Imports CvPoint = OpenCvSharp.Point
Imports WpfPoint = System.Windows.Point

Public Class AlgorithmPage

    Public Sub New()
        InitializeComponent()

        AddHandler Me.Loaded, AddressOf AlgorithmPage_Loaded

        AddHandler LanguageManager.LanguageChanged, AddressOf RefreshLanguageUI


    End Sub

    ' =========================
    ' Mat
    ' =========================
    Private _srcMat As Mat
    Private _templateMat As Mat
    Private _matchMat As Mat

    ' =========================
    ' ROI
    ' =========================
    Private _roiCtrl As RoiController
    Private _roi As CvRect

    ' =========================
    ' Loaded
    ' =========================
    Private Sub AlgorithmPage_Loaded(sender As Object, e As RoutedEventArgs)

        ' =========================
        ' OCR 永遠初始化（一定要先做）
        ' =========================
        _ocr = New PaddleOcrService()

        _decoder = New BarcodeDecodeService()

        ' =========================
        ' Template restore（可選）
        ' =========================
        Dim lastTemplatePath = LastTemplateStore.Load()

        If Not String.IsNullOrWhiteSpace(lastTemplatePath) Then

            Dim data = TemplateManager.LoadTemplate(lastTemplatePath)

            If data IsNot Nothing Then
                ApplyTemplate(data.Template, data.Config)
                TemplateStatusText.Text = "已回復"
            End If

        End If

        RefreshLanguageUI()

    End Sub

    ' =========================
    ' Load Image
    ' =========================
    Private Sub LoadSource_Click(sender As Object, e As RoutedEventArgs)

        SafeRun(Sub()

                    Dim path = DialogHelper.OpenImage()
                    If String.IsNullOrWhiteSpace(path) Then Return

                    _srcMat = ImageFileService.Load(path)
                    SrcImage.Source = ImageConvertHelper.ToBitmap(_srcMat)

                    _roiCtrl = New RoiController(RoiCanvas, SrcImage, _srcMat)

                    ResetUI()

                End Sub)

    End Sub
    ' =========================
    ' 縮放
    ' =========================
    Private zoom As Double = 1.0
    Private pan As WpfPoint = New WpfPoint(0, 0)

    Private isPanning As Boolean = False
    Private lastPanPoint As WpfPoint
    Private Sub Viewer_MouseWheel(sender As Object, e As MouseWheelEventArgs)

        Dim oldZoom = zoom

        If e.Delta > 0 Then
            zoom *= 1.1
        Else
            zoom *= 0.9
        End If

        zoom = Math.Max(0.5, Math.Min(3.0, zoom))

        ImageScale.ScaleX = zoom
        ImageScale.ScaleY = zoom

    End Sub

    Private Sub Viewer_MouseDown(sender As Object, e As MouseButtonEventArgs)

        If e.MiddleButton = MouseButtonState.Pressed Then
            isPanning = True
            lastPanPoint = e.GetPosition(ViewerBorder)
            ViewerBorder.Cursor = Cursors.Hand
        End If

    End Sub

    Private Sub Viewer_MouseMove(sender As Object, e As MouseEventArgs)

        If Not isPanning Then Return

        Dim pos = e.GetPosition(ViewerBorder)

        Dim dx = pos.X - lastPanPoint.X
        Dim dy = pos.Y - lastPanPoint.Y

        ImageTranslate.X += dx
        ImageTranslate.Y += dy

        lastPanPoint = pos

    End Sub

    Private Sub Viewer_MouseUp(sender As Object, e As MouseButtonEventArgs)

        If e.MiddleButton = MouseButtonState.Released Then
            isPanning = False
            ViewerBorder.Cursor = Cursors.Arrow
        End If

    End Sub
    '觸控調優
    Private Sub ImageHost_ManipulationDelta(sender As Object, e As ManipulationDeltaEventArgs)

        zoom *= e.DeltaManipulation.Scale.X

        zoom = Math.Max(0.2, Math.Min(5.0, zoom))

        ImageScale.ScaleX = zoom
        ImageScale.ScaleY = zoom

    End Sub

    ' =========================
    ' ROI events
    ' =========================
    Private Sub RoiCanvas_MouseLeftButtonDown(sender As Object, e As MouseButtonEventArgs)
        _roiCtrl?.MouseDown(e)
    End Sub

    Private Sub RoiCanvas_MouseMove(sender As Object, e As MouseEventArgs)
        _roiCtrl?.MouseMove(e)
    End Sub

    Private Sub RoiCanvas_MouseLeftButtonUp(sender As Object, e As MouseButtonEventArgs)
        _roiCtrl?.MouseUp()

        _roi = _roiCtrl.Roi
        RoiStatusText.Text = "已選擇"
    End Sub

    ' =========================
    ' Create Template
    ' =========================
    Private Sub CreateTemplate_Click(sender As Object, e As RoutedEventArgs)

        SafeRun(Sub()

                    If _srcMat Is Nothing Then Return
                    If _roi.Width <= 0 OrElse _roi.Height <= 0 Then Return

                    Dim preview As Mat = Nothing

                    _templateMat =
                        TemplateMatcher.CreateTemplate(_srcMat, _roi, preview)

                    TemplateImage.Source =
                        ImageConvertHelper.ToBitmap(preview)

                    TemplateStatusText.Text = "模板生成"

                End Sub)

    End Sub

    ' =========================
    ' Match image
    ' =========================
    Private Sub LoadMatch_Click(sender As Object, e As RoutedEventArgs)

        SafeRun(Sub()

                    If _templateMat Is Nothing Then Return

                    Dim path = DialogHelper.OpenImage()
                    If String.IsNullOrWhiteSpace(path) Then Return

                    _matchMat = ImageFileService.Load(path)

                    RunTemplateMatch()

                End Sub)

    End Sub

    ' =========================
    ' Save template + session
    ' =========================
    Private Sub SaveTemplate_Click(sender As Object, e As RoutedEventArgs)

        SafeRun(Sub()

                    If _templateMat Is Nothing Then Return

                    Dim config As New TemplateConfig With {
                        .Threshold = ThresholdSlider.Value,
                        .MatchMethod = MatchMethodBox.SelectedIndex,
                        .RoiX = _roi.X,
                        .RoiY = _roi.Y,
                        .RoiW = _roi.Width,
                        .RoiH = _roi.Height,
                        .EnableOcr = True,
                        .OcrExpectedText = RoiText.Text,
                        .EnableBarcode = True,
                        .BarcodeExpectedText = ResultText.Text
                    }

                    Dim path = TemplateManager.SaveTemplate(_templateMat, config)
                    If String.IsNullOrWhiteSpace(path) Then Return

                    Dim snapshot As New TemplateSnapshot With {
            .TemplatePath = path,
            .Threshold = config.Threshold,
            .MatchMethod = config.MatchMethod,
            .RoiX = config.RoiX,
            .RoiY = config.RoiY,
            .RoiW = config.RoiW,
            .RoiH = config.RoiH
        }

                    TemplateSnapshotStore.Save(snapshot)
                    LastTemplateStore.Save(path)

                    MessageBox.Show("模板 + Snapshot 已保存")

                End Sub)

    End Sub

    ' =========================
    ' Load template manually
    ' =========================
    Private Sub LoadTemplate_Click(sender As Object, e As RoutedEventArgs)

        SafeRun(Sub()

                    Dim data = TemplateManager.LoadTemplate()
                    If data Is Nothing Then Return

                    ApplyTemplate(data.Template, data.Config)

                    LastTemplateStore.Save(data.TemplatePath)

                    MessageBox.Show("模板載入成功")

                End Sub)

    End Sub


    ' =========================
    ' Core apply function
    ' =========================
    Private Sub ApplyTemplate(mat As Mat, config As TemplateConfig)

        _templateMat = mat

        TemplateImage.Source = ImageConvertHelper.ToBitmap(mat)

        ThresholdSlider.Value = config.Threshold
        MatchMethodBox.SelectedIndex = config.MatchMethod

        _roi = New CvRect(
            config.RoiX,
            config.RoiY,
            config.RoiW,
            config.RoiH
        )

    End Sub

    ' =========================
    ' Run match
    ' =========================
    Private Sub RunTemplateMatch()

        If _matchMat Is Nothing OrElse _templateMat Is Nothing Then Return

        Dim result = TemplateMatcher.Match(
            _matchMat,
            _templateMat,
            ThresholdSlider.Value,
            MatchMethodBox.SelectedIndex
        )

        ScoreText.Text = result.Score.ToString("0.000")
        ResultText.Text = If(result.IsOk, "OK", "NG")

        ResultImage.Source =
            ImageConvertHelper.ToBitmap(result.ResultImage)

    End Sub

    ' =========================
    ' Reset UI
    ' =========================
    Private Sub ResetUI()

        RoiCanvas.Children.Clear()

        TemplateImage.Source = Nothing
        ResultImage.Source = Nothing

        ScoreText.Text = ""
        ResultText.Text = "--"

        RoiStatusText.Text = "未選擇"
        TemplateStatusText.Text = "未生成"

    End Sub

    ' OCR
    Private _ocr As PaddleOcrService

    Private Sub OcrRegion_Click(sender As Object, e As RoutedEventArgs)

        Try
            If _srcMat Is Nothing Then
                MessageBox.Show("請先載入圖片")
                Return
            End If

            If _roi.Width <= 0 OrElse _roi.Height <= 0 Then
                MessageBox.Show("請先畫ROI")
                Return
            End If

            ' =========================
            ' ROI crop（顯示用）
            ' =========================
            Dim roiMat As New Mat(_srcMat, _roi)
            ResultImage.Source = ImageConvertHelper.ToBitmap(roiMat)

            ' =========================
            ' OCR
            ' =========================
            Dim text As String = _ocr.RunRoi(_srcMat, _roi)

            ' =========================
            ' =========================
            If String.IsNullOrWhiteSpace(text) Then
                text = "[OCR EMPTY]"
            End If

            RoiText.Text = text
            ScoreText.Text = "OCR Done"

            MessageBox.Show("OCR結果：" & text)

        Catch ex As Exception
            MessageBox.Show(ex.Message)
        End Try

    End Sub

    ' decode
    Private _decoder As BarcodeDecodeService
    Private Sub DecodeRegion_Click(sender As Object, e As RoutedEventArgs)

        Try

            If _srcMat Is Nothing Then
                MessageBox.Show("請先載入圖片")
                Return
            End If

            If _roi.Width <= 0 OrElse _roi.Height <= 0 Then
                MessageBox.Show("請先畫ROI")
                Return
            End If

            Dim text As String = _decoder.RunRoi(_srcMat, _roi)

            If String.IsNullOrWhiteSpace(text) Then
                ResultText.Text = "未識別"
                MessageBox.Show("沒有讀到條碼")
            Else
                ResultText.Text = text
                MessageBox.Show("讀碼成功：" & text)
            End If

        Catch ex As Exception
            MessageBox.Show("錯誤：" & ex.Message)
        End Try

    End Sub

    ' =========================
    ' Safe run
    ' =========================
    Private Sub SafeRun(action As Action)

        Try
            action()
        Catch ex As Exception
            ExceptionHelper.ShowError(ex)
        End Try

    End Sub

    Public Sub RefreshLanguageUI()

        TxtTitle.Text =
            LanguageManager.T("Algo_Title")

        BtnLoadSource.Content =
            LanguageManager.T("Algo_LoadSource")

        BtnCreateTemplate.Content =
            LanguageManager.T("Algo_CreateTemplate")

        BtnLoadMatch.Content =
            LanguageManager.T("Algo_LoadMatch")

    End Sub

End Class