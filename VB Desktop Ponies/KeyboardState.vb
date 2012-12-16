﻿''' <summary>
''' Finds out what keys are currently pressed.
''' </summary>
Public Module KeyboardState
    <System.Runtime.InteropServices.DllImport("user32")>
    Private Function GetKeyState(ByVal vKey As Integer) As Short
    End Function

    Public Function IsKeyPressed(ByVal key As System.Windows.Forms.Keys) As Boolean
        If Not OperatingSystemInfo.IsWindows Then
            Return False
        End If

        Return HighBitSet(GetKeyState(CInt(key)))
    End Function

    Private Function HighBitSet(ByVal keyState As Short) As Boolean
        Return (keyState And &H80) = &H80
    End Function
End Module