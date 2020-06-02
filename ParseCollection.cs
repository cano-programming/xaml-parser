using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using Xamarin.Forms;

namespace Code.Service.Render
{
    public partial class RenderService
    {
        private Func<Task<(Action on, Action off, object value)>> ParseCollection( Type type,
            XNodeInfo xinfo, PageObject page, ParseInfo parseInfo )
        {
            Log.Information( "PARSE ELEMENT {name} as ICollection or IEnumerable", xinfo.Name );

            var createObject = CreateInstance( xinfo, type, out ParseInfo createInfo );

            var pinfo = parseInfo.Merge( createInfo );

            var createProperty = ParseElementProperty( xinfo, page, type, pinfo );

            List<(XNodeInfo xinfo, Func<Task<(Action on, Action off, object value)>> res)> items =
                xinfo.Elements.Value.Select( v => (v, ParseElement( v, page, pinfo )) ).ToList( );

            return async ( ) =>
            {
                var collection = createObject( );
                
                var res1 = await createProperty( collection );
                (Action on, Action off) res2 = default;

                items.ForEach( async v =>
                {
                    var resLoc = res2;
                    var res = await v.res.Invoke( );

                    var action = FindAddMethod( collection, res.value.GetType( ), v.xinfo );
                    if( action != null )
                    {
                        res2 = (JoinAction( resLoc.on, res.on ), JoinAction( resLoc.off, res.off ));

                        if( collection is ResourceDictionary && v.xinfo.XamlKey != null )
                        {
                            page.ResourceDictionary.Add( v.xinfo.XamlKey, res.value );
                        }

                        action.Invoke( res.value );
                    }
                } );

                return (JoinAction( res1.on, res2.on ), JoinAction( res1.off, res2.off ), collection);
            };
        }
    }
}
