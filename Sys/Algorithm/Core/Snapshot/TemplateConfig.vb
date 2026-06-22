Public Class TemplateConfig

    ' =========================
    ' Match
    ' =========================
    Public Property Threshold As Double
    Public Property MatchMethod As Integer

    ' =========================
    ' ROI
    ' =========================
    Public Property RoiX As Integer
    Public Property RoiY As Integer
    Public Property RoiW As Integer
    Public Property RoiH As Integer

    ' =========================
    ' OCR
    ' =========================
    Public Property EnableOcr As Boolean = False

    Public Property OcrExpectedText As String = ""

    ' =========================
    ' Barcode
    ' =========================
    Public Property EnableBarcode As Boolean = False

    Public Property BarcodeExpectedText As String = ""

End Class