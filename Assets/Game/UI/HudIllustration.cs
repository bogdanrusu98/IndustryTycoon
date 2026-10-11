using UnityEngine;
using UnityEngine.UI;

namespace IndustryTycoon.UI
{
    /// <summary>Layered, resolution independent artwork for the three HUD emblems.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class HudIllustration : MaskableGraphic
    {
        public enum Emblem { Coin, Star, Flag }
        [SerializeField] private Emblem emblem;
        public void Configure(Emblem value) { emblem = value; raycastTarget = false; SetVerticesDirty(); }
        private static readonly Color Gold = new Color(1f, .68f, .025f);
        private static readonly Color Light = new Color(1f, .91f, .38f);
        private static readonly Color Edge = new Color(.48f, .23f, .025f);

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            if (emblem == Emblem.Coin)
            {
                Disc(mesh, .50f, .46f, .46f, new Color(.09f,.16f,.13f,.28f));
                Disc(mesh, .50f, .51f, .45f, Edge);
                Disc(mesh, .50f, .53f, .42f, Light);
                Disc(mesh, .50f, .50f, .38f, new Color(.94f,.43f,.015f));
                Disc(mesh, .50f, .52f, .34f, Gold);
                Disc(mesh, .49f, .55f, .29f, new Color(1f,.76f,.06f));
                Star(mesh, new Vector2(.5f,.50f), .225f, Edge);
                Star(mesh, new Vector2(.49f,.525f), .20f, Light);
            }
            else if (emblem == Emblem.Star)
            {
                Star(mesh, new Vector2(.51f,.46f), .49f, new Color(.08f,.14f,.12f,.3f));
                Star(mesh, new Vector2(.5f,.52f), .48f, Edge);
                Star(mesh, new Vector2(.5f,.535f), .44f, Light);
                Star(mesh, new Vector2(.5f,.515f), .38f, Gold);
            }
            else
            {
                Quad(mesh,.15f,.08f,.25f,.92f,Edge);
                Quad(mesh,.16f,.12f,.23f,.92f,Gold);
                Disc(mesh,.195f,.91f,.045f,Light);
                Polygon(mesh, new[]{new Vector2(.23f,.86f),new Vector2(.48f,.88f),new Vector2(.64f,.80f),new Vector2(.88f,.82f),new Vector2(.88f,.44f),new Vector2(.64f,.42f),new Vector2(.47f,.50f),new Vector2(.23f,.48f)},Gold);
                Quad(mesh,.24f,.79f,.46f,.85f,Light);
                Quad(mesh,.165f,.15f,.185f,.86f,Light);
            }
        }
        private Vector2 P(Vector2 p) => rectTransform.rect.min + Vector2.Scale(p,rectTransform.rect.size);
        private void Triangle(VertexHelper m,Vector2 a,Vector2 b,Vector2 c,Color tint)
        {
            int n=m.currentVertCount;
            tint *= color;
            m.AddVert(P(a),tint,Vector2.zero);m.AddVert(P(b),tint,Vector2.zero);m.AddVert(P(c),tint,Vector2.zero);
            m.AddTriangle(n,n+1,n+2);
        }
        private void Quad(VertexHelper m,float x,float y,float r,float t,Color tint) =>
            Polygon(m,new[]{new Vector2(x,y),new Vector2(x,t),new Vector2(r,t),new Vector2(r,y)},tint);
        private void Polygon(VertexHelper m,Vector2[] points,Color tint)
        {
            for(int i=1;i<points.Length-1;i++)Triangle(m,points[0],points[i],points[i+1],tint);
        }
        private void Disc(VertexHelper m,float x,float y,float radius,Color tint)
        {
            Vector2 center=new Vector2(x,y);
            for(int i=0;i<64;i++)Triangle(m,center,center+Unit(i*Mathf.PI/32)*radius,center+Unit((i+1)*Mathf.PI/32)*radius,tint);
        }
        private static Vector2 Unit(float a) => new Vector2(Mathf.Cos(a),Mathf.Sin(a));
        private void Star(VertexHelper m,Vector2 center,float radius,Color tint)
        {
            for(int i=0;i<10;i++)Triangle(m,center,center+Unit(i*Mathf.PI/5+Mathf.PI/2)*radius*(i%2==0?1:.48f),center+Unit((i+1)*Mathf.PI/5+Mathf.PI/2)*radius*(i%2==0?.48f:1),tint);
        }
    }
}
