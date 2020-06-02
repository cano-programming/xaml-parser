using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using Xamarin.Forms;

namespace Code.Service.Render
{
    public partial class RenderService
    {
        private Func<Task<(Action on, Action off, object value)>> ParseLayoutView( Type type,
            XNodeInfo xinfo, PageObject page, ParseInfo parseInfo )
        {
            Log.Information( "PARSE ELEMENT {name} as ILayoutController", xinfo.Name );

            var createObject = CreateInstance( xinfo, type, out ParseInfo createInfo );

            var pinfo = parseInfo.Merge( createInfo );

            Func<Layout<View>, Task<(Action on, Action off)>> createElement = ( layout ) => DefaultTask2Action;

            foreach( var child in xinfo.Elements.Value )
            {
                var res = ParseElement( child, page, pinfo );
                var locCreate = createElement;

                createElement = async ( layout ) =>
                {
                    var res1 = await locCreate.Invoke( layout );
                    var res2 = await res.Invoke( );

                    if( res2.value != null )
                        layout.Children.Add( res2.value as View );

                    return (JoinAction( res1.on, res2.on ), JoinAction( res1.off, res2.off ));
                };
            }

            var createProperty = ParseElementProperty( xinfo, page, type, pinfo );

            return async ( ) =>
           {
               var layout = createObject( ) as Layout<View>;

               var res1 = await createProperty( layout );
               var res2 = await createElement( layout );

               return (JoinAction( res1.on, res2.on ), JoinAction( res1.off, res2.off ), layout);
           };
        }
    }
}
