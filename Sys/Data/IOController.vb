Imports System
Imports System.IO
Imports System.Media
Imports System.Threading
Imports System.Threading.Tasks
Imports MetroSystemForDotNet.IoBoard

Public Class IOController
    Implements IDisposable

#Region "Fields"

    Private _buzzer As ModbusBuzzer
    Private ReadOnly _sem As New SemaphoreSlim(1, 1)
    Private _cts As CancellationTokenSource

    Private ReadOnly _log As Action(Of String)

    Private ReadOnly _ip As String
    Private ReadOnly _port As Integer
    Private ReadOnly _unitId As Byte
    Private ReadOnly _coilBase As UShort

#End Region

#Region "Ctor"

    Public Sub New(ip As String,
                   port As Integer,
                   unitId As Byte,
                   coilBase As UShort,
                   log As Action(Of String))

        _ip = ip
        _port = port
        _unitId = unitId
        _coilBase = coilBase
        If log Is Nothing Then
            _log = Sub(x As String)
                   End Sub
        Else
            _log = log
        End If

    End Sub

#End Region

#Region "Init"

    Public Async Function InitializeAsync() As Task
        Try
            _buzzer = New ModbusBuzzer(_ip, _port, _unitId, _coilBase)

            _buzzer.Logger = Sub(s)
                                 _log("[Buzzer] " & s)
                             End Sub

            Await Task.Run(Sub() _buzzer.Connect())

            _log("IO卡初始化完成")

        Catch ex As Exception
            _log("IO初始化失敗: " & ex.Message)
            SafeDispose()
        End Try
    End Function

#End Region

#Region "Public API"

    Public Sub Trigger(label As String)
        If String.IsNullOrWhiteSpace(label) Then Return

        label = label.Trim().ToUpperInvariant()

        If label.Contains("OK") Then
            HandleOK()
        ElseIf label.Contains("NG") Then
            HandleNG()
        End If
    End Sub

#End Region

#Region "OK / NG"

    Private Sub HandleOK()

        SetTowerLight(False, False, True)
        PlayVoice("Correct.wav")

        Try
            _buzzer?.SetCoil(0, True)
            _buzzer?.SetCoil(1, False)
            _buzzer?.SetCoil(3, False)
        Catch
        End Try

        _log("收到OK")

    End Sub

    Private Sub HandleNG()

        SetTowerLight(True, False, False)

        Task.Run(Async Function()

                     Try
                         PlayVoice("Error.wav")

                         _buzzer?.SetCoil(3, True)
                         Await Task.Delay(2000)

                         _buzzer?.SetCoil(3, False)
                         _buzzer?.SetCoil(0, False)
                         _buzzer?.SetCoil(1, True)

                     Catch
                     End Try

                 End Function)

        _log("收到NG")

    End Sub

#End Region

#Region "Tower Light"

    Public Sub SetTowerLight(red As Boolean, yellow As Boolean, green As Boolean)

        Try
            If _buzzer Is Nothing Then Return

            _log($"燈號 R={red} Y={yellow} G={green}")

            _buzzer.SetDO(1, green)
            _buzzer.SetDO(2, red)
            _buzzer.SetDO(3, yellow)

        Catch ex As Exception
            _log("燈號失敗: " & ex.Message)
        End Try

    End Sub

#End Region

#Region "Voice"

    Private Sub PlayVoice(fileName As String)

        Try
            Dim filePath = System.IO.Path.Combine(
    AppDomain.CurrentDomain.BaseDirectory,
    "Voice",
    fileName
)

            If Not File.Exists(filePath) Then
                _log("語音不存在: " & filePath)
                Return
            End If

            Using player As New SoundPlayer(filePath)
                player.Play()
            End Using

        Catch ex As Exception
            _log("語音錯誤: " & ex.Message)
        End Try

    End Sub

#End Region

#Region "Dispose"

    Public Sub Dispose() Implements IDisposable.Dispose
        SafeDispose()
    End Sub

    Private Sub SafeDispose()

        Try
            _buzzer?.Dispose()
        Catch
        End Try

        Try
            _cts?.Cancel()
        Catch
        End Try

        Try
            _cts?.Dispose()
        Catch
        End Try

        _sem.Dispose()

    End Sub

#End Region

End Class