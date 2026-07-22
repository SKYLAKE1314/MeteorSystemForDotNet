Public Class TemplateSnapshot

    Public Property TemplatePath As String
    Public Property CameraDeviceId As String ' 建模時使用的相機 DeviceId

    Public Property Threshold As Double
    Public Property MatchMethod As Integer

    Public Property RoiX As Integer
    Public Property RoiY As Integer
    Public Property RoiW As Integer
    Public Property RoiH As Integer

    ' =========================
    ' OCR
    ' =========================
    Public Property EnableOcr As Boolean
    Public Property OcrExpectedText As String
    ' OCR engine 原始辨識結果（供回溯與修訂用）
    Public Property OcrRecognizedText As String

    ' =========================
    ' Barcode
    ' =========================
    Public Property EnableBarcode As Boolean
    Public Property BarcodeExpectedText As String
    ' 條碼/解碼引擎原始結果
    Public Property BarcodeDecodedText As String

    ' =========================
    ' Revision history (模板修訂記錄)
    ' =========================
    Public Property Revisions As List(Of TemplateRevision)

    ' =========================
    ' Vision params
    ' =========================
    Public Property PyramidLevel As Integer
    Public Property MinArea As Integer

    Public Property CannyLow As Integer
    Public Property CannyHigh As Integer

    Public Property AngleMin As Double
    Public Property AngleMax As Double
    Public Property AngleStep As Double

    Public Property ScaleTolerance As Double ' 大小範圍容許度 (例如 0.1 表示 +- 10% 縮放)

End Class