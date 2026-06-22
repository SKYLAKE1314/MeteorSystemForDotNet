Imports Microsoft.ML.OnnxRuntime
Imports Microsoft.ML.OnnxRuntime.Tensors
Imports OpenCvSharp

Public Class Yolo26Detector

    Private ReadOnly _session As InferenceSession

    Private ReadOnly _inputWidth As Integer = 640

    Private ReadOnly _inputHeight As Integer = 640

    Public Property ScoreThreshold As Single = 0.25F

    Public Sub New(modelPath As String)

        Dim opt As New SessionOptions()

        opt.AppendExecutionProvider_OpenVINO("GPU")

        opt.GraphOptimizationLevel =
            GraphOptimizationLevel.ORT_ENABLE_ALL

        _session =
            New InferenceSession(
                modelPath,
                opt)

    End Sub

    Public Function Detect(
        src As Mat
    ) As List(Of DetectionResult)

        Dim scale As Double

        Dim padX As Integer

        Dim padY As Integer

        Dim tensor =
            CreateTensor(
                src,
                scale,
                padX,
                padY)

        Dim inputName =
            _session.InputMetadata.Keys.First()

        Dim inputs =
            New List(Of NamedOnnxValue)

        inputs.Add(
            NamedOnnxValue.CreateFromTensor(
                inputName,
                tensor))

        Dim result =
            _session.Run(inputs)

        Dim output =
            result.First().
            AsTensor(Of Single)

        Return ParseResult(
            output,
            src.Width,
            src.Height,
            scale,
            padX,
            padY)

    End Function

    Private Function CreateTensor(
        src As Mat,
        ByRef scale As Double,
        ByRef padX As Integer,
        ByRef padY As Integer
    ) As DenseTensor(Of Single)

        Dim resized As New Mat()

        scale =
            Math.Min(
                _inputWidth / src.Width,
                _inputHeight / src.Height)

        Dim newW =
            CInt(src.Width * scale)

        Dim newH =
            CInt(src.Height * scale)

        Cv2.Resize(
            src,
            resized,
            New Size(newW, newH))

        padX =
            (_inputWidth - newW) \ 2

        padY =
            (_inputHeight - newH) \ 2

        Dim letterbox As New Mat(
            New Size(
                _inputWidth,
                _inputHeight),
            MatType.CV_8UC3,
            Scalar.All(114))

        resized.CopyTo(
            New Mat(
                letterbox,
                New Rect(
                    padX,
                    padY,
                    newW,
                    newH)))

        Cv2.CvtColor(
            letterbox,
            letterbox,
            ColorConversionCodes.BGR2RGB)

        Dim tensor =
            New DenseTensor(Of Single)(
                {1, 3,
                 _inputHeight,
                 _inputWidth})

        For y = 0 To _inputHeight - 1

            For x = 0 To _inputWidth - 1

                Dim pixel =
                    letterbox.
                    At(Of Vec3b)(y, x)

                tensor(0, 0, y, x) =
                    pixel.Item0 / 255.0F

                tensor(0, 1, y, x) =
                    pixel.Item1 / 255.0F

                tensor(0, 2, y, x) =
                    pixel.Item2 / 255.0F

            Next

        Next

        Return tensor

    End Function

    Private Function ParseResult(
        output As Tensor(Of Single),
        imageW As Integer,
        imageH As Integer,
        scale As Double,
        padX As Integer,
        padY As Integer
    ) As List(Of DetectionResult)

        Dim list As New List(Of DetectionResult)

        Dim dims = output.Dimensions.ToArray()
        Dim count = dims(1)

        For i = 0 To count - 1

            Dim score =
                output(0, i, 4)

            If score <
                ScoreThreshold Then

                Continue For

            End If

            Dim cls =
                CInt(output(0, i, 5))

            Dim x1 =
                output(0, i, 0)

            Dim y1 =
                output(0, i, 1)

            Dim x2 =
                output(0, i, 2)

            Dim y2 =
                output(0, i, 3)

            x1 =
                CSng((x1 - padX) / scale)

            y1 =
                CSng((y1 - padY) / scale)

            x2 =
                CSng((x2 - padX) / scale)

            y2 =
                CSng((y2 - padY) / scale)

            x1 =
                Math.Max(0, x1)

            y1 =
                Math.Max(0, y1)

            x2 =
                Math.Min(imageW - 1, x2)

            y2 =
                Math.Min(imageH - 1, y2)

            list.Add(
                New DetectionResult With {
                    .ClassId = cls,
                    .score = score,
                    .X = x1,
                    .Y = y1,
                    .Width = x2 - x1,
                    .Height = y2 - y1
                })

        Next

        Return list

    End Function

End Class