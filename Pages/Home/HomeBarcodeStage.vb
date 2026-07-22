Imports System.IO
Imports System.Text
Imports System.Threading
Imports System.Windows
Imports System.Windows.Threading
Imports MetroSystemForDotNet
Imports Microsoft.Web.WebView2.Core
Imports Microsoft.Win32
Imports OpenCvSharp
Imports OpenCvSharp.WpfExtensions
Imports Cv = OpenCvSharp

Partial Class HomePage

    Private Async Function WaitBarcodeResultAsync(snapshot As TemplateSnapshot, timeoutMs As Integer) As Task(Of String)
        Dim decoder = AppRuntime.Barcode
        If decoder Is Nothing Then Return ""

        If snapshot IsNot Nothing AndAlso Not snapshot.EnableBarcode Then
            Logger.Info("[FLOW] 條碼解碼未啟用，直接跳過")
            Return Nothing
        End If

        Dim expText = snapshot?.BarcodeExpectedText?.Trim()?.ToLower()
        If String.IsNullOrWhiteSpace(expText) OrElse expText = "--" OrElse expText = "未識別" OrElse expText = "未辨識" OrElse expText = "barcode empty" Then
            Logger.Info("[FLOW] 條碼預期文字為空或為預設佔位符，直接跳過")
            Return Nothing
        End If

        Dim cameraId = _ocrCameraId
        If String.IsNullOrWhiteSpace(cameraId) Then cameraId = _matchCameraId
        If String.IsNullOrWhiteSpace(cameraId) Then cameraId = GetCamId(0)
        If String.IsNullOrWhiteSpace(cameraId) Then Return ""

        CameraService.Instance.StartCamera(cameraId)

        Dim sw As New Stopwatch()
        sw.Start()

        Const DecodeIntervalMs As Long = 100
        Dim lastAttempt As Long = -DecodeIntervalMs
        Dim decoding As Boolean = False
        Dim resultBox As String = Nothing

        Dim frameHandler As Action(Of String, BitmapSource) =
            Sub(frameId As String, frameBmp As BitmapSource)
                If Not CameraManager.IsSameDevice(frameId, cameraId) Then Return
                Dim winRef = _activePreviewWin
                If winRef IsNot Nothing Then
                    winRef.UpdateFrame(frameBmp)
                End If
            End Sub

        ' 取得切換相機前最後一張畫面作為佔位圖，避免高畫質相機暖機時（長達數秒）發生黑畫面
        Dim placeholderBmp As BitmapSource = _lastFrameBitmap
        Dim lastProcessedFrame As BitmapSource = Nothing

        Try
            ' 1. 在 UI 執行緒建立並顯示無邊距彈窗
            Dispatcher.Invoke(Sub()
                                  _activePreviewWin = New LivePreviewWindow()
                                  If placeholderBmp IsNot Nothing Then
                                      _activePreviewWin.UpdateFrame(placeholderBmp)
                                  End If
                                  _activePreviewWin.UpdateOcrResult("條碼辨識中...")
                                  _activePreviewWin.Show()
                              End Sub)
            AddHandler CameraService.Instance.FrameArrived, frameHandler

            While sw.ElapsedMilliseconds < timeoutMs

                If IsSkipRequested(DetectionFlowStage.Barcode) Then
                    Logger.Info("[FLOW] 解碼已跳過")
                    Return Nothing
                End If

                If resultBox IsNot Nothing Then
                    Logger.Info($"[FLOW] 解碼成功: {resultBox}")
                    Return resultBox
                End If

                Dim elapsed = sw.ElapsedMilliseconds
                If Not decoding AndAlso elapsed - lastAttempt >= DecodeIntervalMs Then
                    lastAttempt = elapsed
                    Dim frame = CameraService.Instance.GetFrame(cameraId)

                    If frame IsNot Nothing AndAlso Not Object.ReferenceEquals(frame, lastProcessedFrame) Then
                        lastProcessedFrame = frame
                        Dim frameCopy = frame

                        Dim matForDecode As Mat = Nothing
                        Try
                            ' 直接使用安全且執行緒安全的 ImageConvertHelper 轉換，避免背景執行緒拋出 ExecutionEngineException
                            matForDecode = ImageConvertHelper.ToMat(frameCopy)
                        Catch ex As Exception
                            Logger.Warn("[FLOW] 影像轉 Mat 失敗: " & ex.Message)
                        End Try

                        If matForDecode IsNot Nothing AndAlso Not matForDecode.IsDisposed Then
                            decoding = True
                            Task.Run(Function()
                                         Try
                                             Using mat = matForDecode
                                                 Dim text As String = ""

                                                 ' 使用與 Algorithm 頁面相同的進階解碼策略，但強制全域解碼（忽略匹配ROI）
                                                 text = decoder.RunAdvanced(mat)

                                                 If Not String.IsNullOrWhiteSpace(text) Then
                                                     resultBox = text.Trim()
                                                     Dim tempText = resultBox
                                                     Dispatcher.Invoke(Sub()
                                                                           If _activePreviewWin IsNot Nothing Then
                                                                               _activePreviewWin.UpdateOcrResult(tempText)
                                                                           End If
                                                                       End Sub)
                                                 End If
                                             End Using
                                         Catch ex As Exception
                                             Logger.Warn("[FLOW] 背景解碼異常: " & ex.Message)
                                         Finally
                                             decoding = False
                                         End Try
                                         Return True
                                     End Function)
                        End If
                    End If
                End If

                Await Task.Delay(5)
            End While

            Logger.Warn("[FLOW] 解碼超時")
            Return ""

        Finally
            ' 取消相機事件訂閱
            Try
                RemoveHandler CameraService.Instance.FrameArrived, frameHandler
            Catch
            End Try
            If _activePreviewWin IsNot Nothing Then
                Dispatcher.Invoke(Sub()
                                      Try
                                          _activePreviewWin.Close()
                                      Catch
                                          ' 靜默處理關閉時的例外
                                      End Try
                                      _activePreviewWin = Nothing
                                  End Sub)
            End If
        End Try
    End Function
End Class
