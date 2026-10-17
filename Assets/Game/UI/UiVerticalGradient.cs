using UnityEngine;
using UnityEngine.UI;

namespace IndustryTycoon.UI
{
    /// <summary>Vertex-only relief for progress fills; no extra draw geometry or polling.</summary>
    [DisallowMultipleComponent]
    public sealed class UiVerticalGradient : BaseMeshEffect
    {
        public override void ModifyMesh(VertexHelper vertices)
        {
            if (!IsActive() || vertices.currentVertCount == 0) return;
            Rect rect = graphic.rectTransform.rect;
            UIVertex vertex = default;
            for (int i = 0; i < vertices.currentVertCount; i++)
            {
                vertices.PopulateUIVertex(ref vertex, i);
                float height = Mathf.InverseLerp(rect.yMin, rect.yMax, vertex.position.y);
                float shade = Mathf.Lerp(.80f, 1f, height);
                Color color = vertex.color;
                color.r *= shade;
                color.g *= shade;
                color.b *= shade;
                vertex.color = color;
                vertices.SetUIVertex(vertex, i);
            }
        }
    }
}
