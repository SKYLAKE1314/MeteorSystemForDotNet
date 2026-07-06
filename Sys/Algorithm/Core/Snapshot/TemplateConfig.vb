Public Class TemplateConfig

    Public Property CameraDeviceId As String ' 建模時使用的相機 DeviceId
    Public Property SupplierCode As String   ' 供應商代碼（用於 StartTask 自動查找模板）
    Public Property Threshold As Double
    Public Property MatchMethod As Integer

    Public Property RoiX As Integer
    Public Property RoiY As Integer
    Public Property RoiW As Integer
    Public Property RoiH As Integer

    Public Property EnableOcr As Boolean
    Public Property OcrExpectedText As String

    Public Property EnableBarcode As Boolean
    Public Property BarcodeExpectedText As String

    Public Property PyramidLevel As Integer
    Public Property MinArea As Integer

    Public Property CannyLow As Integer
    Public Property CannyHigh As Integer

    Public Property AngleMin As Double
    Public Property AngleMax As Double
    Public Property AngleStep As Double

End Class