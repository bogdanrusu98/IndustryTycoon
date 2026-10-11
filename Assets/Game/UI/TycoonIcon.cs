using UnityEngine;
using UnityEngine.UI;

namespace IndustryTycoon.UI
{
    public enum TycoonGlyph
    {
        Coin,
        Crate,
        Factory,
        Flag,
        Star,
        Routes,
        Market,
        Gem,
        Arrow,
        Wood,
        Plank,
        IronOre,
        IronBar,
        Tool,
        MachinePart,
        Pause
    }

    /// <summary>Small atlas-free, resolution-independent UI silhouettes. No Update or raycasting.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class TycoonIcon : MaskableGraphic
    {
        [SerializeField] private TycoonGlyph glyph;
        public TycoonGlyph Glyph { get => glyph; set { glyph = value; SetVerticesDirty(); } }
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            switch (glyph)
            {
                case TycoonGlyph.Coin:
                    Disc(vh, .5f, .5f, .43f); break;
                case TycoonGlyph.Crate:
                    Quad(vh,.12f,.14f,.88f,.26f);Quad(vh,.12f,.74f,.88f,.86f);
                    Quad(vh,.12f,.26f,.24f,.74f);Quad(vh,.76f,.26f,.88f,.74f);
                    Triangle(vh,new Vector2(.24f,.26f),new Vector2(.24f,.42f),new Vector2(.76f,.74f));
                    Triangle(vh,new Vector2(.24f,.26f),new Vector2(.76f,.74f),new Vector2(.76f,.58f));break;
                case TycoonGlyph.Factory:
                    Quad(vh,.1f,.15f,.9f,.55f); Quad(vh,.15f,.55f,.29f,.9f);
                    Triangle(vh,new Vector2(.32f,.55f),new Vector2(.60f,.55f),new Vector2(.60f,.8f));
                    Triangle(vh,new Vector2(.6f,.55f),new Vector2(.9f,.55f),new Vector2(.9f,.8f)); break;
                case TycoonGlyph.Flag:
                    Quad(vh,.2f,.12f,.29f,.9f); Quad(vh,.29f,.52f,.82f,.86f); break;
                case TycoonGlyph.Star:
                    for(int i=0;i<10;i++) {
                        float a=i*Mathf.PI/5+Mathf.PI/2, b=(i+1)*Mathf.PI/5+Mathf.PI/2;
                        Triangle(vh,new Vector2(.5f,.5f),new Vector2(.5f,.5f)+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*(i%2==0?.46f:.22f),new Vector2(.5f,.5f)+new Vector2(Mathf.Cos(b),Mathf.Sin(b))*(i%2==0?.22f:.46f));
                    } break;
                case TycoonGlyph.Routes:
                    Quad(vh,.12f,.59f,.7f,.71f); Triangle(vh,new Vector2(.65f,.48f),new Vector2(.9f,.65f),new Vector2(.65f,.82f));
                    Quad(vh,.3f,.29f,.88f,.41f); Triangle(vh,new Vector2(.35f,.18f),new Vector2(.1f,.35f),new Vector2(.35f,.52f)); break;
                case TycoonGlyph.Market:
                    Quad(vh,.18f,.12f,.82f,.58f); Quad(vh,.1f,.62f,.9f,.82f); break;
                case TycoonGlyph.Gem:
                    Triangle(vh,new Vector2(.12f,.6f),new Vector2(.5f,.12f),new Vector2(.88f,.6f));
                    Quad(vh,.25f,.6f,.75f,.85f); break;
                case TycoonGlyph.Wood:
                    Quad(vh,.18f,.38f,.82f,.62f); Disc(vh,.18f,.5f,.12f); Disc(vh,.82f,.5f,.12f); break;
                case TycoonGlyph.Plank:
                    Quad(vh,.12f,.28f,.88f,.48f); Quad(vh,.12f,.54f,.88f,.74f); break;
                case TycoonGlyph.IronOre:
                    Triangle(vh,new Vector2(.12f,.22f),new Vector2(.34f,.82f),new Vector2(.55f,.25f));
                    Triangle(vh,new Vector2(.42f,.22f),new Vector2(.68f,.9f),new Vector2(.9f,.22f)); break;
                case TycoonGlyph.IronBar:
                    Quad(vh,.14f,.26f,.86f,.52f); Quad(vh,.22f,.54f,.78f,.74f); break;
                case TycoonGlyph.Tool:
                    Quad(vh,.43f,.14f,.57f,.68f); Quad(vh,.22f,.62f,.78f,.78f); break;
                case TycoonGlyph.MachinePart:
                    Disc(vh,.5f,.5f,.42f); Disc(vh,.5f,.5f,.16f); break;
                case TycoonGlyph.Pause:
                    Quad(vh,.24f,.16f,.42f,.84f); Quad(vh,.58f,.16f,.76f,.84f); break;
                default:
                    Quad(vh,.12f,.42f,.68f,.58f); Triangle(vh,new Vector2(.55f,.18f),new Vector2(.9f,.5f),new Vector2(.55f,.82f)); break;
            }
        }
        private Vector2 Point(Vector2 p) { Rect r=rectTransform.rect; return r.min+Vector2.Scale(p,r.size); }
        private void Triangle(VertexHelper v,Vector2 a,Vector2 b,Vector2 c) {
            int n=v.currentVertCount;
            v.AddVert(Point(a),color,Vector2.zero); v.AddVert(Point(b),color,Vector2.zero); v.AddVert(Point(c),color,Vector2.zero);
            v.AddTriangle(n,n+1,n+2);
        }
        private void Quad(VertexHelper v,float x,float y,float right,float top) {
            Triangle(v,new Vector2(x,y),new Vector2(x,top),new Vector2(right,top));
            Triangle(v,new Vector2(x,y),new Vector2(right,top),new Vector2(right,y));
        }
        private void Disc(VertexHelper v,float x,float y,float radius) {
            for(int i=0;i<24;i++) { float a=i*Mathf.PI/12,b=(i+1)*Mathf.PI/12;
                Triangle(v,new Vector2(x,y),new Vector2(x+Mathf.Cos(a)*radius,y+Mathf.Sin(a)*radius),new Vector2(x+Mathf.Cos(b)*radius,y+Mathf.Sin(b)*radius)); }
        }
    }
}
