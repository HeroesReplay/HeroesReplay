using System;
using System.Collections.Generic;

namespace HeroesReplay.Core.YouTube.Outbox;

/// <summary>
/// The resumable upload session URI YouTube returns repeats every query parameter of the request
/// that started it, and Google.Apis puts the API key there as <c>key=</c> (#368). The key never
/// goes to disk: the saved URI drops it (<see cref="WithoutKey"/>), and the send and the session
/// status query add the configured key back (<see cref="WithKey"/>), so YouTube gets the same
/// request it got before.
/// </summary>
public static class UploadSessionUri
{
    public const string KeyParameter = "key";

    public static bool HasKey(string sessionUri)
    {
        if (!Split(sessionUri, out _, out string query, out _))
        {
            return false;
        }

        foreach (string parameter in query.Split('&'))
        {
            if (IsKey(parameter))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The URI without any <c>key</c> parameter. Every other parameter stays as it was.</summary>
    public static string WithoutKey(string sessionUri)
    {
        if (!Split(sessionUri, out string head, out string query, out string fragment))
        {
            return sessionUri;
        }

        var kept = new List<string>();
        foreach (string parameter in query.Split('&'))
        {
            if (parameter.Length > 0 && !IsKey(parameter))
            {
                kept.Add(parameter);
            }
        }

        return kept.Count == 0 ? head + fragment : head + "?" + string.Join('&', kept) + fragment;
    }

    /// <summary>
    /// The URI to send to: the saved one with <paramref name="apiKey"/> as its <c>key</c>
    /// parameter, the way Google.Apis started the session. No key configured means none is sent.
    /// </summary>
    public static string WithKey(string sessionUri, string apiKey)
    {
        string bare = WithoutKey(sessionUri);
        if (string.IsNullOrEmpty(bare) || string.IsNullOrWhiteSpace(apiKey))
        {
            return bare;
        }

        int hash = bare.IndexOf('#', StringComparison.Ordinal);
        string head = hash < 0 ? bare : bare[..hash];
        string fragment = hash < 0 ? string.Empty : bare[hash..];
        string separator = head.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return head + separator + KeyParameter + "=" + Uri.EscapeDataString(apiKey) + fragment;
    }

    private static bool Split(string uri, out string head, out string query, out string fragment)
    {
        head = uri;
        query = null;
        fragment = string.Empty;
        if (string.IsNullOrEmpty(uri))
        {
            return false;
        }

        int question = uri.IndexOf('?', StringComparison.Ordinal);
        if (question < 0)
        {
            return false;
        }

        int hash = uri.IndexOf('#', question);
        head = uri[..question];
        query = hash < 0 ? uri[(question + 1)..] : uri[(question + 1)..hash];
        fragment = hash < 0 ? string.Empty : uri[hash..];
        return true;
    }

    private static bool IsKey(string parameter)
    {
        int equals = parameter.IndexOf('=', StringComparison.Ordinal);
        string name = equals < 0 ? parameter : parameter[..equals];
        return string.Equals(
            Uri.UnescapeDataString(name),
            KeyParameter,
            StringComparison.OrdinalIgnoreCase
        );
    }
}
