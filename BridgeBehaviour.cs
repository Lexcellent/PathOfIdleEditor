using System;
using UnityEngine;

namespace PathOfIdleEditor;

internal sealed class BridgeBehaviour : MonoBehaviour
{
    public BridgeBehaviour(IntPtr pointer) : base(pointer)
    {
    }

    private void Update() => BridgeServer.ProcessPendingRequests();

    private void OnDestroy() => BridgeServer.Stop();
}
