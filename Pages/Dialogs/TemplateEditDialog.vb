Imports System.Windows
Imports System.Windows.Controls
Imports System.Windows.Media

Public Class TemplateEditDialog
    Inherits System.Windows.Window

    Public Property Snapshot As TemplateSnapshot

    Private TbOcrRecognized As TextBox
    Private TbOcrExpected As TextBox
    Private TbBarcodeDecoded As TextBox
    Private TbBarcodeExpected As TextBox
    Private TbComment As TextBox
    Private ChkEnableOcr As CheckBox
    Private ChkEnableBarcode As CheckBox
    Private BtnSave As Button
    Private BtnCancel As Button

    Public Sub New(snapshot As TemplateSnapshot, preview As ImageSource)
        Me.Title = "模板編輯"
        Me.Width = 650
        Me.Height = 520
        Me.WindowStartupLocation = WindowStartupLocation.CenterOwner
        Me.ResizeMode = ResizeMode.CanResize
        Me.Background = New SolidColorBrush(Color.FromRgb(&HF3, &HF3, &HF3))

        If snapshot Is Nothing Then
            snapshot = New TemplateSnapshot()
        End If

        Me.Snapshot = snapshot

        Dim tbBg As New SolidColorBrush(Color.FromRgb(&HF2, &HEC, &HFF))
        Dim tbBorder As New SolidColorBrush(Color.FromRgb(&H9A, &H6B, &HD8))
        Dim tbFg As New SolidColorBrush(Color.FromRgb(&H3C, &H1E, &H66))

        ' ===== Root Grid with proper styling =====
        Dim root As New Grid()
        root.Background = Brushes.White

        ' Row definitions
        Dim row1 As New RowDefinition() With {.Height = New GridLength(1, GridUnitType.Star)}
        Dim row2 As New RowDefinition() With {.Height = GridLength.Auto}
        root.RowDefinitions.Add(row1)
        root.RowDefinitions.Add(row2)

        ' ===== Top scrollable content area =====
        Dim scrollViewer As New ScrollViewer()
        scrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        scrollViewer.Margin = New Thickness(20)

        Dim contentPanel As New StackPanel()
        scrollViewer.Content = contentPanel

        ' ===== Title =====
        Dim titleBlock As New TextBlock()
        titleBlock.Text = "編輯模板參數"
        titleBlock.FontSize = 18
        titleBlock.FontWeight = FontWeights.SemiBold
        titleBlock.Foreground = New SolidColorBrush(Color.FromRgb(&H20, &H20, &H20))
        titleBlock.Margin = New Thickness(0, 0, 0, 10)
        contentPanel.Children.Add(titleBlock)

        ' ===== OCR Section =====
        Dim ocrHeaderPanel As New StackPanel()
        ocrHeaderPanel.Orientation = Orientation.Horizontal
        ocrHeaderPanel.Margin = New Thickness(0, 10, 0, 5)

        Dim ocrLabel As New TextBlock()
        ocrLabel.Text = "OCR 識別結果"
        ocrLabel.FontSize = 12
        ocrLabel.FontWeight = FontWeights.SemiBold
        ocrLabel.Foreground = New SolidColorBrush(Color.FromRgb(&H40, &H40, &H40))
        ocrLabel.VerticalAlignment = VerticalAlignment.Center

        ChkEnableOcr = New CheckBox()
        ChkEnableOcr.Content = "啟用 OCR"
        ChkEnableOcr.IsChecked = Snapshot.EnableOcr
        ChkEnableOcr.Margin = New Thickness(15, 0, 0, 0)
        ChkEnableOcr.VerticalAlignment = VerticalAlignment.Center

        ocrHeaderPanel.Children.Add(ocrLabel)
        ocrHeaderPanel.Children.Add(ChkEnableOcr)

        contentPanel.Children.Add(ocrHeaderPanel)

        Dim ocrGrid As New Grid()

        ' 2 rows（label row + input row）
        ocrGrid.RowDefinitions.Add(New RowDefinition() With {.Height = GridLength.Auto})
        ocrGrid.RowDefinitions.Add(New RowDefinition() With {.Height = GridLength.Auto})

        ' 3 columns（left / gap / right）
        ocrGrid.ColumnDefinitions.Add(New ColumnDefinition() With {.Width = New GridLength(1, GridUnitType.Star)})
        ocrGrid.ColumnDefinitions.Add(New ColumnDefinition() With {.Width = New GridLength(10)})
        ocrGrid.ColumnDefinitions.Add(New ColumnDefinition() With {.Width = New GridLength(1, GridUnitType.Star)})

        Dim ocrOrigLabel As New TextBlock()
        ocrOrigLabel.Text = "原始"
        ocrOrigLabel.FontSize = 11
        ocrOrigLabel.Foreground = New SolidColorBrush(Color.FromRgb(&H70, &H70, &H70))
        Grid.SetColumn(ocrOrigLabel, 0)
        ocrGrid.Children.Add(ocrOrigLabel)

        TbOcrRecognized = New TextBox()
        TbOcrRecognized.Height = 110
        TbOcrRecognized.TextWrapping = TextWrapping.Wrap
        TbOcrRecognized.AcceptsReturn = True
        TbOcrRecognized.VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        TbOcrRecognized.IsReadOnly = True
        TbOcrRecognized.Background = tbBg
        TbOcrRecognized.Foreground = tbFg
        TbOcrRecognized.BorderThickness = New Thickness(1)
        TbOcrRecognized.BorderBrush = tbBorder
        Grid.SetColumn(TbOcrRecognized, 0)
        Grid.SetRow(TbOcrRecognized, 1)
        ocrGrid.Children.Add(TbOcrRecognized)

        Dim ocrOrigRow As New RowDefinition() With {.Height = GridLength.Auto}
        ocrGrid.RowDefinitions.Add(ocrOrigRow)

        Dim ocrExpectLabel As New TextBlock()
        ocrExpectLabel.Text = "期望"
        ocrExpectLabel.FontSize = 11
        ocrExpectLabel.Foreground = New SolidColorBrush(Color.FromRgb(&H70, &H70, &H70))
        Grid.SetColumn(ocrExpectLabel, 2)
        ocrGrid.Children.Add(ocrExpectLabel)

        TbOcrExpected = New TextBox()
        TbOcrExpected.Height = 110
        TbOcrExpected.TextWrapping = TextWrapping.Wrap
        TbOcrExpected.AcceptsReturn = True
        TbOcrExpected.VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        TbOcrExpected.Background = tbBg
        TbOcrExpected.Foreground = tbFg
        TbOcrExpected.BorderThickness = New Thickness(1)
        TbOcrExpected.BorderBrush = tbBorder
        Grid.SetColumn(TbOcrExpected, 2)
        Grid.SetRow(TbOcrExpected, 1)
        ocrGrid.Children.Add(TbOcrExpected)

        contentPanel.Children.Add(ocrGrid)

        ' ===== Barcode Section =====
        Dim barcodeHeaderPanel As New StackPanel()
        barcodeHeaderPanel.Orientation = Orientation.Horizontal
        barcodeHeaderPanel.Margin = New Thickness(0, 10, 0, 5)

        Dim barcodeLabel As New TextBlock()
        barcodeLabel.Text = "條碼識別結果"
        barcodeLabel.FontSize = 12
        barcodeLabel.FontWeight = FontWeights.SemiBold
        barcodeLabel.Foreground = New SolidColorBrush(Color.FromRgb(&H40, &H40, &H40))
        barcodeLabel.VerticalAlignment = VerticalAlignment.Center

        ChkEnableBarcode = New CheckBox()
        ChkEnableBarcode.Content = "啟用解碼"
        ChkEnableBarcode.IsChecked = Snapshot.EnableBarcode
        ChkEnableBarcode.Margin = New Thickness(15, 0, 0, 0)
        ChkEnableBarcode.VerticalAlignment = VerticalAlignment.Center

        barcodeHeaderPanel.Children.Add(barcodeLabel)
        barcodeHeaderPanel.Children.Add(ChkEnableBarcode)

        contentPanel.Children.Add(barcodeHeaderPanel)

        Dim barcodeGrid As New Grid()
        barcodeGrid.ColumnDefinitions.Add(New ColumnDefinition() With {.Width = New GridLength(1, GridUnitType.Star)})
        barcodeGrid.ColumnDefinitions.Add(New ColumnDefinition() With {.Width = New GridLength(10)})
        barcodeGrid.ColumnDefinitions.Add(New ColumnDefinition() With {.Width = New GridLength(1, GridUnitType.Star)})
        barcodeGrid.Margin = New Thickness(0, 0, 0, 15)

        Dim barcodeOrigLabel As New TextBlock()
        barcodeOrigLabel.Text = "原始"
        barcodeOrigLabel.FontSize = 11
        barcodeOrigLabel.Foreground = New SolidColorBrush(Color.FromRgb(&H70, &H70, &H70))
        Grid.SetColumn(barcodeOrigLabel, 0)
        barcodeGrid.Children.Add(barcodeOrigLabel)

        TbBarcodeDecoded = New TextBox()
        TbBarcodeDecoded.Height = 40
        TbBarcodeDecoded.TextWrapping = TextWrapping.Wrap
        TbBarcodeDecoded.IsReadOnly = True
        TbBarcodeDecoded.Background = tbBg
        TbBarcodeDecoded.Foreground = tbFg
        TbBarcodeDecoded.BorderThickness = New Thickness(1)
        TbBarcodeDecoded.BorderBrush = tbBorder
        Grid.SetColumn(TbBarcodeDecoded, 0)
        Grid.SetRow(TbBarcodeDecoded, 1)
        barcodeGrid.Children.Add(TbBarcodeDecoded)

        Dim barcodeOrigRow As New RowDefinition() With {.Height = GridLength.Auto}
        barcodeGrid.RowDefinitions.Add(barcodeOrigRow)

        Dim barcodeExpectLabel As New TextBlock()
        barcodeExpectLabel.Text = "期望"
        barcodeExpectLabel.FontSize = 11
        barcodeExpectLabel.Foreground = New SolidColorBrush(Color.FromRgb(&H70, &H70, &H70))
        Grid.SetColumn(barcodeExpectLabel, 2)
        barcodeGrid.Children.Add(barcodeExpectLabel)

        TbBarcodeExpected = New TextBox()
        TbBarcodeExpected.Height = 40
        TbBarcodeExpected.TextWrapping = TextWrapping.Wrap
        TbBarcodeExpected.Background = tbBg
        TbBarcodeExpected.Foreground = tbFg
        TbBarcodeExpected.BorderThickness = New Thickness(1)
        TbBarcodeExpected.BorderBrush = tbBorder
        Grid.SetColumn(TbBarcodeExpected, 2)
        Grid.SetRow(TbBarcodeExpected, 1)
        barcodeGrid.Children.Add(TbBarcodeExpected)

        contentPanel.Children.Add(barcodeGrid)

        ' ===== Comment Section =====
        Dim commentLabel As New TextBlock()
        commentLabel.Text = "修訂說明"
        commentLabel.FontSize = 12
        commentLabel.FontWeight = FontWeights.SemiBold
        commentLabel.Foreground = New SolidColorBrush(Color.FromRgb(&H40, &H40, &H40))
        commentLabel.Margin = New Thickness(0, 10, 0, 5)
        contentPanel.Children.Add(commentLabel)

        TbComment = New TextBox()
        TbComment.Height = 60
        TbComment.TextWrapping = TextWrapping.Wrap
        TbComment.AcceptsReturn = True
        TbComment.Background = tbBg
        TbComment.Foreground = tbFg
        TbComment.BorderThickness = New Thickness(1)
        TbComment.BorderBrush = tbBorder
        TbComment.Padding = New Thickness(8)
        contentPanel.Children.Add(TbComment)

        Grid.SetRow(scrollViewer, 0)
        root.Children.Add(scrollViewer)

        ' ===== Bottom button bar =====
        Dim buttonBar As New Border()
        buttonBar.Background = New SolidColorBrush(Color.FromRgb(&HF8, &HF8, &HF8))
        buttonBar.BorderThickness = New Thickness(0, 1, 0, 0)
        buttonBar.BorderBrush = New SolidColorBrush(Color.FromRgb(&HE0, &HE0, &HE0))
        buttonBar.Padding = New Thickness(20, 12, 20, 12)
        Grid.SetRow(buttonBar, 1)

        Dim btnPanel As New StackPanel()
        btnPanel.Orientation = Orientation.Horizontal
        btnPanel.HorizontalAlignment = HorizontalAlignment.Right

        BtnCancel = New Button()
        BtnCancel.Content = "取消"
        BtnCancel.Width = 80
        BtnCancel.Height = 32
        BtnCancel.Foreground = Brushes.Black
        BtnCancel.Background = New SolidColorBrush(Color.FromRgb(&HE8, &HE8, &HE8))
        BtnCancel.BorderThickness = New Thickness(1)
        BtnCancel.BorderBrush = New SolidColorBrush(Color.FromRgb(&HD0, &HD0, &HD0))
        BtnCancel.Margin = New Thickness(0, 0, 8, 0)
        btnPanel.Children.Add(BtnCancel)

        BtnSave = New Button()
        BtnSave.Content = "保存"
        BtnSave.Width = 80
        BtnSave.Height = 32
        BtnSave.Foreground = Brushes.White
        BtnSave.Background = New SolidColorBrush(Color.FromRgb(&H0, &H78, &HD4))
        BtnSave.BorderThickness = New Thickness(0)
        btnPanel.Children.Add(BtnSave)

        buttonBar.Child = btnPanel
        root.Children.Add(buttonBar)

        Me.Content = root

        ' ===== Initialize text =====
        TbOcrRecognized.Text = If(snapshot.OcrRecognizedText, "")
        TbOcrExpected.Text = If(snapshot.OcrExpectedText, "")
        TbBarcodeDecoded.Text = If(snapshot.BarcodeDecodedText, "")
        TbBarcodeExpected.Text = If(snapshot.BarcodeExpectedText, "")

        AddHandler BtnCancel.Click, AddressOf BtnCancel_Click
        AddHandler BtnSave.Click, AddressOf BtnSave_Click
    End Sub

    ' 實時更新 OCR 原始文本
    Public Sub UpdateOcrText(text As String)
        If String.IsNullOrEmpty(text) Then Return
        TbOcrRecognized.Text = text
        If String.IsNullOrEmpty(TbOcrExpected.Text) Then
            TbOcrExpected.Text = text
        End If
    End Sub

    ' 實時更新 Barcode 原始文本
    Public Sub UpdateBarcodeText(text As String)
        If String.IsNullOrEmpty(text) Then Return
        TbBarcodeDecoded.Text = text
        If String.IsNullOrEmpty(TbBarcodeExpected.Text) Then
            TbBarcodeExpected.Text = text
        End If
    End Sub

    ' 將編輯的內容更新回 snapshot
    Public Sub UpdateSnapshot(snapshot As TemplateSnapshot)
        If snapshot Is Nothing Then Return
        snapshot.OcrRecognizedText = TbOcrRecognized.Text
        snapshot.OcrExpectedText = TbOcrExpected.Text
        snapshot.BarcodeDecodedText = TbBarcodeDecoded.Text
        snapshot.BarcodeExpectedText = TbBarcodeExpected.Text
        snapshot.EnableOcr = ChkEnableOcr.IsChecked.GetValueOrDefault(False)
        snapshot.EnableBarcode = ChkEnableBarcode.IsChecked.GetValueOrDefault(False)
    End Sub

    Private Sub BtnCancel_Click(sender As Object, e As RoutedEventArgs)
        Me.DialogResult = False
    End Sub

    Private Sub BtnSave_Click(sender As Object, e As RoutedEventArgs)
        Try
            Snapshot.OcrRecognizedText = TbOcrRecognized.Text
            Snapshot.OcrExpectedText = TbOcrExpected.Text
            Snapshot.BarcodeDecodedText = TbBarcodeDecoded.Text
            Snapshot.BarcodeExpectedText = TbBarcodeExpected.Text
            Snapshot.EnableOcr = ChkEnableOcr.IsChecked.GetValueOrDefault(False)
            Snapshot.EnableBarcode = ChkEnableBarcode.IsChecked.GetValueOrDefault(False)

            If Snapshot.Revisions Is Nothing Then
                Snapshot.Revisions = New List(Of TemplateRevision)()
            End If

            Dim rev As New TemplateRevision()
            rev.RevisionId = Guid.NewGuid().ToString()
            rev.Timestamp = DateTimeOffset.Now.ToUnixTimeMilliseconds()
            rev.TemplatePath = Snapshot.TemplatePath
            rev.Threshold = Snapshot.Threshold
            rev.RoiX = Snapshot.RoiX
            rev.RoiY = Snapshot.RoiY
            rev.RoiW = Snapshot.RoiW
            rev.RoiH = Snapshot.RoiH
            rev.OcrExpectedText = Snapshot.OcrExpectedText
            rev.BarcodeExpectedText = Snapshot.BarcodeExpectedText
            rev.Comment = TbComment.Text
            rev.Author = Environment.UserName

            Snapshot.Revisions.Add(rev)

            Me.DialogResult = True

        Catch ex As Exception
            MeteorMessageBox.Show("保存失敗: " & ex.Message)
            Me.DialogResult = False
        End Try
    End Sub
End Class
