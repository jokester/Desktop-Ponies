﻿Imports System.Globalization
Imports System.IO

Public Class StringCollectionParser
    Private Const NotParsed = "This value was not parsed due to an earlier parsing failure."
    Private Const NullFailure = "This value has not been specified."
    Private Const BooleanFailureFormat = "Could not interpret '{0}' as a Boolean value. Valid values are '{1}' or '{2}'."
    Private Const IntegerFailureFormat = "Could not interpret '{0}' as a integer value."
    Private Const FloatFailureFormat = "Could not interpret '{0}' as a decimal value."
    Private Const MapFailureFormat = "'{0}' is not a valid value. Valid values are: {1}."
    Private Const Vector2FailureFormat =
        "Could not interpret '{0}' as a point. Points consist of two integers separated by a comma, e.g. '0,0'."
    Private Const PathNullFailureFormat = "The specified path was null."
    Private Const PathInvalidCharsFailureFormat = "This path contains invalid characters: {0}"
    Private Const PathRootedFailureFormat = "A relative path must be used, an absolute path is not allowed: {0}"
    Private Const FileNotFoundFailureFormat = "This file does not exist, or cannot be accessed: {0}"
    Private Const OutOfRangeFailureFormat = "Value of {0} is out of range. Value must be between {1} and {2} (inclusive)."

    Public ReadOnly Issues As New List(Of ParseIssue)()
    Private items As String()
    Private itemNames As String()
    Private index As Integer
    Private lastParseFailed As Boolean
    Public ReadOnly Property AllParsingSuccessful As Boolean
        Get
            Return Not lastParseFailed
        End Get
    End Property
    Public Sub New(itemsToParse As String(), itemNames As String())
        Argument.EnsureNotNull(itemsToParse, "elements")
        Argument.EnsureNotNull(itemNames, "itemNames")
        items = itemsToParse
        Me.itemNames = itemNames
    End Sub
    Private Function GetNextItem() As String
        Dim item As String = Nothing
        If index < items.Length Then item = items(index)
        index += 1
        Return item
    End Function
    Private Function HandleParsed(Of T)(parsed As Parsed(Of T)) As T
        If parsed.Result <> ParseResult.Success Then
            lastParseFailed = parsed.Result = ParseResult.Failed
            If Not (parsed.Source Is Nothing AndAlso parsed.Result = ParseResult.Fallback) Then
                Dim i = index - 1
                Issues.Add(New ParseIssue(i,
                                          If(i < itemNames.Length, itemNames(i), Nothing),
                                          parsed.Source,
                                          If(lastParseFailed, Nothing, parsed.Value.ToString()),
                                          parsed.Reason))
            End If
        End If
        Return parsed.Value
    End Function
    Private Function SkipParse(Of T)() As T
        Return HandleParsed(Parsed.Failed(Of T)(GetNextItem(), NotParsed))
    End Function
    Public Function NoParse() As String
        Return HandleParsed(Parsed.Success(GetNextItem()))
    End Function
    Public Function NotNull() As String
        Return NotNull(Nothing)
    End Function
    Public Function NotNull(fallback As String) As String
        If lastParseFailed Then Return SkipParse(Of String)()
        Return HandleParsed(ParsedNotNull(GetNextItem(), fallback))
    End Function
    Private Function ParsedNotNull(s As String, fallback As String) As Parsed(Of String)
        If s IsNot Nothing Then
            Return Parsed.Success(s)
        ElseIf fallback IsNot Nothing Then
            Return Parsed.Fallback(s, fallback, NullFailure)
        Else
            Return Parsed.Failed(Of String)(s, NullFailure)
        End If
    End Function
    Public Function ParseBoolean() As Boolean
        Return ParseBoolean(Nothing)
    End Function
    Public Function ParseBoolean(fallback As Boolean?) As Boolean
        If lastParseFailed Then Return SkipParse(Of Boolean)()
        Return HandleParsed(ParsedBoolean(GetNextItem(), fallback))
    End Function
    Private Function ParsedBoolean(s As String, fallback As Boolean?) As Parsed(Of Boolean)
        Dim result As Boolean
        If Boolean.TryParse(s, result) Then
            Return Parsed.Success(result)
        ElseIf fallback IsNot Nothing Then
            Return Parsed.Fallback(s, fallback.Value, String.Format(BooleanFailureFormat, s, Boolean.TrueString, Boolean.FalseString))
        Else
            Return Parsed.Failed(Of Boolean)(s, String.Format(BooleanFailureFormat, s, Boolean.TrueString, Boolean.FalseString))
        End If
    End Function
    Public Function ParseInt32() As Integer
        Return ParseInt32(Integer.MinValue, Integer.MaxValue)
    End Function
    Public Function ParseInt32(fallback As Integer) As Integer
        Return ParseInt32(fallback, Integer.MinValue, Integer.MaxValue)
    End Function
    Public Function ParseInt32(min As Integer, max As Integer) As Integer
        Return ParseInt32Internal(Nothing, min, max)
    End Function
    Public Function ParseInt32(fallback As Integer, min As Integer, max As Integer) As Integer
        Return ParseInt32Internal(fallback, min, max)
    End Function
    Private Function ParseInt32Internal(fallback As Integer?, min As Integer, max As Integer) As Integer
        If lastParseFailed Then Return SkipParse(Of Integer)()
        Return HandleParsed(ParsedInt32(GetNextItem(), fallback, min, max))
    End Function
    Private Function ParsedInt32(s As String, fallback As Integer?, Optional min As Integer = Integer.MinValue, Optional max As Integer = Integer.MaxValue) As Parsed(Of Integer)
        If min > max Then Throw New ArgumentException("min must be less than or equal to max.")
        Dim failReason As String = Nothing
        Dim result As Integer
        If Not Integer.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, result) Then
            failReason = String.Format(IntegerFailureFormat, s)
        End If
        If failReason Is Nothing AndAlso (result < min OrElse result > max) Then
            failReason = String.Format(OutOfRangeFailureFormat, result, min, max)
        End If
        If failReason Is Nothing Then
            Return Parsed.Success(result)
        ElseIf fallback IsNot Nothing Then
            Return Parsed.Fallback(s, fallback.Value, failReason)
        Else
            Return Parsed.Failed(Of Integer)(s, failReason)
        End If
    End Function
    Public Function ParseDouble() As Double
        Return ParseDouble(Double.MinValue, Double.MaxValue)
    End Function
    Public Function ParseDouble(fallback As Double) As Double
        Return ParseDouble(fallback, Double.MinValue, Double.MaxValue)
    End Function
    Public Function ParseDouble(min As Double, max As Double) As Double
        Return ParseDoubleInternal(Nothing, min, max)
    End Function
    Public Function ParseDouble(fallback As Double, min As Double, max As Double) As Double
        Return ParseDoubleInternal(fallback, min, max)
    End Function
    Private Function ParseDoubleInternal(fallback As Double?, min As Double, max As Double) As Double
        If lastParseFailed Then Return SkipParse(Of Double)()
        Return HandleParsed(ParsedDouble(GetNextItem(), fallback, min, max))
    End Function
    Private Function ParsedDouble(s As String, fallback As Double?, min As Double, max As Double) As Parsed(Of Double)
        If min > max Then Throw New ArgumentException("min must be less than or equal to max.")
        Dim failReason As String = Nothing
        Dim result As Double
        If Not Double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, result) Then
            failReason = String.Format(FloatFailureFormat, s)
        End If
        If failReason Is Nothing AndAlso (result < min OrElse result > max) Then
            failReason = String.Format(OutOfRangeFailureFormat, result, min, max)
        End If
        If failReason Is Nothing Then
            Return Parsed.Success(result)
        ElseIf fallback IsNot Nothing Then
            Return Parsed.Fallback(s, fallback.Value, failReason)
        Else
            Return Parsed.Failed(Of Double)(s, failReason)
        End If
    End Function
    Public Function Map(Of T As Structure)(mapping As IDictionary(Of String, T)) As T
        Return Map(Of T)(mapping, Nothing)
    End Function
    Public Function Map(Of T As Structure)(mapping As IDictionary(Of String, T), fallback As T?) As T
        If lastParseFailed Then Return SkipParse(Of T)()
        Return HandleParsed(ParsedMap(GetNextItem(), mapping, fallback))
    End Function
    Private Function ParsedMap(Of T As Structure)(s As String, mapping As IDictionary(Of String, T), fallback As T?) As Parsed(Of T)
        Dim result As T
        If s IsNot Nothing AndAlso mapping.TryGetValue(s, result) Then
            Return Parsed.Success(result)
        ElseIf fallback IsNot Nothing Then
            Return Parsed.Fallback(s, fallback.Value, String.Format(MapFailureFormat, s, String.Join(", ", mapping.Keys)))
        Else
            Return Parsed.Failed(Of T)(s, String.Format(MapFailureFormat, s, String.Join(", ", mapping.Keys)))
        End If
    End Function
    Public Function ParseVector2() As Vector2
        Return ParseVector2(Nothing)
    End Function
    Public Function ParseVector2(fallback As Vector2?) As Vector2
        If lastParseFailed Then Return SkipParse(Of Vector2)()
        Return HandleParsed(ParsedVector2(GetNextItem(), fallback))
    End Function
    Private Function ParsedVector2(s As String, fallback As Vector2?) As Parsed(Of Vector2)
        Dim parts As String() = Nothing
        If s IsNot Nothing Then parts = s.Split(","c)
        Dim x As Integer
        Dim y As Integer
        If parts IsNot Nothing AndAlso parts.Length = 2 AndAlso
            Integer.TryParse(parts(0), NumberStyles.Integer, CultureInfo.InvariantCulture, x) AndAlso
            Integer.TryParse(parts(1), NumberStyles.Integer, CultureInfo.InvariantCulture, y) Then
            Return Parsed.Success(New Vector2(x, y))
        ElseIf fallback IsNot Nothing Then
            Return Parsed.Fallback(s, fallback.Value, String.Format(Vector2FailureFormat, s))
        Else
            Return Parsed.Failed(Of Vector2)(s, String.Format(Vector2FailureFormat, s))
        End If
    End Function
    Public Function SpecifiedCombinePath(pathPrefix As String, source As String) As String
        Return SpecifiedCombinePath(pathPrefix, source, Nothing)
    End Function
    Public Function SpecifiedCombinePath(pathPrefix As String, source As String, fallback As String) As String
        If lastParseFailed Then Return SkipParse(Of String)()
        Return HandleParsed(ParsedCombinePath(pathPrefix, source, fallback))
    End Function
    Private Function ParsedCombinePath(pathPrefix As String, s As String, fallback As String) As Parsed(Of String)
        Dim failReasonFormat As String = Nothing
        If s Is Nothing Then failReasonFormat = PathNullFailureFormat
        If failReasonFormat Is Nothing AndAlso
            (s.IndexOfAny(Path.GetInvalidPathChars()) <> -1 OrElse
            s.IndexOfAny(Path.GetInvalidFileNameChars()) <> -1) Then
            failReasonFormat = PathInvalidCharsFailureFormat
        End If
        If failReasonFormat Is Nothing AndAlso Path.IsPathRooted(s) Then failReasonFormat = PathRootedFailureFormat
        If failReasonFormat Is Nothing Then
            Return Parsed.Success(Path.Combine(pathPrefix, s))
        ElseIf fallback IsNot Nothing Then
            Return Parsed.Fallback(s, fallback, String.Format(failReasonFormat, s))
        Else
            Return Parsed.Failed(Of String)(s, String.Format(failReasonFormat, s))
        End If
    End Function
    Public Sub SpecifiedFileExists(filePath As String)
        SpecifiedFileExists(filePath, Nothing)
    End Sub
    Public Sub SpecifiedFileExists(filePath As String, fallback As String)
        If lastParseFailed Then Return
        If File.Exists(filePath) Then
            HandleParsed(Parsed.Success(filePath))
        ElseIf fallback IsNot Nothing Then
            HandleParsed(Parsed.Fallback(filePath, fallback, String.Format(FileNotFoundFailureFormat, filePath)))
        Else
            HandleParsed(Parsed.Failed(Of String)(filePath, String.Format(FileNotFoundFailureFormat, filePath)))
        End If
    End Sub
    Public Function Assert(source As String, condition As Func(Of String, Boolean), reason As String, fallback As String) As Boolean
        If lastParseFailed Then Return False
        Dim result = condition(source)
        If result Then
            HandleParsed(Parsed.Success(source))
        ElseIf fallback IsNot Nothing Then
            HandleParsed(Parsed.Fallback(source, fallback, reason))
        Else
            HandleParsed(Parsed.Failed(Of String)(source, reason))
        End If
        Return result
    End Function

    Private Enum ParseResult
        Success
        Fallback
        Failed
    End Enum
    Private Class Parsed
        Private Sub New()
        End Sub
        Public Shared Function Success(Of T)(value As T) As Parsed(Of T)
            Return New Parsed(Of T)(value)
        End Function
        Public Shared Function Fallback(Of T)(source As String, value As T, reason As String) As Parsed(Of T)
            Return New Parsed(Of T)(source, value, reason)
        End Function
        Public Shared Function Failed(Of T)(source As String, reason As String) As Parsed(Of T)
            Return New Parsed(Of T)(source, reason)
        End Function
    End Class
    Private Structure Parsed(Of T)
        Private _source As String
        Private _value As T
        Private _reason As String
        Private _result As ParseResult
        Public ReadOnly Property Source As String
            Get
                If _result = ParseResult.Success Then Throw New InvalidOperationException("Cannot get source for a successful parse.")
                Return _source
            End Get
        End Property
        Public ReadOnly Property Value As T
            Get
                Return _value
            End Get
        End Property
        Public ReadOnly Property Reason As String
            Get
                Return _reason
            End Get
        End Property
        Public ReadOnly Property Result As ParseResult
            Get
                Return _result
            End Get
        End Property
        Public Sub New(_value As T)
            Me._value = _value
            Me._result = ParseResult.Success
        End Sub
        Public Sub New(_source As String, _value As T, _reason As String)
            Me._source = _source
            Me._value = _value
            Me._reason = _reason
            Me._result = ParseResult.Fallback
        End Sub
        Public Sub New(_source As String, _reason As String)
            Me._source = _source
            Me._reason = _reason
            Me._result = ParseResult.Failed
        End Sub
    End Structure
End Class

Public Structure ParseIssue
    Private _index As Integer
    Private _propertyName As String
    Private _source As String
    Private _fallbackValue As String
    Private _reason As String
    Public ReadOnly Property Fatal As Boolean
        Get
            Return _fallbackValue Is Nothing
        End Get
    End Property
    Public ReadOnly Property Index As Integer
        Get
            Return _index
        End Get
    End Property
    Public ReadOnly Property PropertyName As String
        Get
            Return _propertyName
        End Get
    End Property
    Public ReadOnly Property Source As String
        Get
            Return _source
        End Get
    End Property
    Public ReadOnly Property FallbackValue As String
        Get
            Return _fallbackValue
        End Get
    End Property
    Public ReadOnly Property Reason As String
        Get
            Return _reason
        End Get
    End Property
    Public Sub New(_propertyName As String, _source As String, _fallbackValue As String, _reason As String)
        Me.New(-1, _propertyName, _source, _fallbackValue, _reason)
    End Sub
    Public Sub New(_index As Integer, _propertyName As String, _source As String, _fallbackValue As String, _reason As String)
        Me._index = _index
        Me._propertyName = _propertyName
        Me._source = _source
        Me._fallbackValue = _fallbackValue
        Me._reason = _reason
    End Sub
End Structure