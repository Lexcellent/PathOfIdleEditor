using System;
using UnityEngine;

namespace PathOfIdleEditor;

internal sealed class BridgeBehaviour : MonoBehaviour
{
    // IL2CPP 注入的 MonoBehaviour 必须保留 IntPtr 构造函数。
    public BridgeBehaviour(IntPtr pointer) : base(pointer)
    {
    }

    // Unity 对象通常不具备线程安全性，因此在每帧主线程中消费桌面端请求。
    private void Update() => BridgeServer.ProcessPendingRequests();

    private void OnDestroy() => BridgeServer.Stop();
}
