using System;

namespace InsectSpace.Gameplay.Modules
{
    [Serializable]
    internal sealed class PhoneCodeRequest
    {
        public string phoneNumber;
    }

    [Serializable]
    internal sealed class PhoneCodeResponse
    {
        public bool accepted;
        public string phoneNumber;
        public string expiresAt;
        public string developmentCode;
    }

    [Serializable]
    internal sealed class PhoneLoginRequest
    {
        public string phoneNumber;
        public string code;
        public bool register = true;
    }

    [Serializable]
    internal sealed class LoginResponse
    {
        public string sessionToken;
        public long playerId;
        public string homeRealmId;
        public string expiresAt;
    }

    [Serializable]
    internal sealed class SessionProfileResponse
    {
        public long playerId;
        public string homeRealmId;
        public string expiresAt;
    }

    [Serializable] internal sealed class EmptyRequest { }
    [Serializable] internal sealed class RevokeResponse { public bool revoked; }

    [Serializable]
    internal sealed class RouteRequest
    {
        public long playerId;
        public string homeRealmId;
        public string preferredClusterId;
        public string preferredSceneId;
    }

    [Serializable]
    internal sealed class WorldRouteResponse
    {
        public string homeRealmId;
        public string worldClusterId;
        public string instanceId;
        public string sceneId;
        public long epoch;
        public string endpoint;
    }
}
