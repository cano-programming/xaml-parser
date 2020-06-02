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
        public class ParseInfo
        {
            public string TargetType;
            public bool IsTemplate;
            public List<BindableInfo> TemplateBindableInfo = new List<BindableInfo>( );
            public IEnumerable<string> PropertyToSkip;

            /// <summary>
            /// Saved last index of property key for quickly generate next index 
            /// </summary>
            public SoftDictionary<string, int> LastPropertyIndex = new SoftDictionary<string, int>( );

            public string GenerageBindableKey( string propertyName )
            {
                LastPropertyIndex.TryGetValue( propertyName, out int idx );
                idx++;
                LastPropertyIndex[propertyName] = idx;

                return $"{propertyName}{idx}";
            }

            public void ResetBindableKeys( )
            {
                LastPropertyIndex.Clear( );
            }
        }

        public const string FormDataScript = "{form-data}";
        public const string XamlNamespace = "http://schemas.microsoft.com/winfx/2009/xaml";
        public const string BarcodeXPath = "/system/barcode";

        protected Task<(Action, Action, object)> DefaultTask2ActionObject 
            = Task.FromResult<(Action, Action, object)>( (null, null, null) );

        protected Task<(Action, Action)> DefaultTask2Action
            = Task.FromResult<(Action, Action)>( (null, null) );

        private Func<Task<(Action on, Action off, object element)>> RunParse( PageObject page )
        {
            var res = ParseElement( new XNodeInfo( page.Xaml ), page );

            return async ( ) =>
            {
                var res1 = await res( );

                void on( )
                {
                    var xdata = _treeService.ResolveXPath( page.XPathData, false );

                    if( xdata.HasResults == false )
                    {
                        throw new Exception( $"No result for {xdata.XPath}" );
                    }

                    var dataResolver = NewXPathResolver( (XElement)xdata.SingleResultUnlocked( ), page );

                    page.DataResolver = dataResolver;
                }

                return (JoinAction( on, res1.on ), res1.off, res1.value);
            };
        }

        /// <summary>
        /// Parse a node with properties and child elements
        /// </summary>
        /// <returns>
        /// Return create instance function with result of turn on/off actions and created object.
        /// To create object need invoke it
        /// </returns>
        private Func<Task<(Action on, Action off, object value)>> ParseElement( XNodeInfo xinfo,
            PageObject page, ParseInfo parseInfo = null )
        {
            var type = FindType( xinfo.Name );
            
            Log.Information( "START PARSE ELEMENT {name}, type {type} {result}",
                xinfo.Name, type.FullName, type == null ? "not found" : "found" );

            if( type == null )
            {
                Log.Warning( "START PARSE ELEMENT {name}, type {type} not found",
                    xinfo.Name, xinfo.Name );
                return ( ) => DefaultTask2ActionObject;
            };
            
            switch( type )
            {
                case var value when type.IsValueType:
                    return ParseValueType( type, xinfo );
                    
                case ICollection collection:
                case var enumerable when type.GetInterface( "IEnumerable" ) != null:
                    return ParseCollection( type, xinfo, page, parseInfo );

                case var itemsView when GetPropertyInfo( null, type, "ItemTemplate",
                                                        typeof( DataTemplate ) )?.CanWrite == true:
                    return ParseItemsView( type, xinfo, page );

                case var layout when type.IsSubclassOf( typeof( Layout<View> ) ):
                    return ParseLayoutView( type, xinfo, page, parseInfo );

                case var template when type.BaseType == typeof( ElementTemplate ):
                    return ParseDataTemplate( xinfo, page, parseInfo );

                case var content when GetPropertyInfo( null, type, "Content" )?.CanWrite == true:
                    return ParseViewContent( content, xinfo, page, parseInfo );

                case var def when type != null:
                    return ParseDefault( type, xinfo, page, parseInfo );

                default:
                    Log.Warning( "START PARSE ELEMENT {name}, type {type} not recognized", 
                        xinfo.Name, type.FullName );
                    return ( ) => DefaultTask2ActionObject;
            }
        }

        /// <summary>
        /// Parse propertirs of node 
        /// </summary>
        /// <returns>
        /// Return create instance function with result of turn on/off actions.
        /// To set object properties need invoke it
        /// </returns>
        private Func<object, Task<(Action on, Action off)>> ParseElementProperty( XNodeInfo xinfo,
            PageObject page, Type elementType, ParseInfo parseInfo = null )
        {
            Func<object, Task<(Action on, Action off)>> retVal = ( elem ) => DefaultTask2Action;

            var propRes = xinfo.Properties.Value.OrderBy( v => v.NameParts.Length ).Select( xprop =>
               {
                   Func<object, Task<(Action on, Action off)>> res = ( elem ) => DefaultTask2Action;

                   //skip property that was parsed (used for creating instance of element)
                   if( parseInfo?.PropertyToSkip?.Any( v => v == xprop.Name ) ?? false ) return res;

                   var prop = GetPropertyInfo( null, elementType, xprop.NameParts );

                   switch( xprop )
                   {
                       case var attached when prop == null && ParseAttachedProperty( attached, out res ):
                           //parsed as AttachedProperty
                           break;

                       case var tapped when prop == null
                           && ParseTapped( elementType, tapped, parseInfo, page, out res ):
                           //parsed as IsTapped property
                           break;

                       case var focused when prop != null && (prop.CanRead && prop.CanWrite == false)
                           && ParseFocused( elementType, focused, parseInfo, page, out res ):
                           //parsed as IsFocused property
                           //by default element has no setter for IsFocused property,
                           //custom elements that have setter, not processing.
                           //They have their own processing logic
                           break;

                       case var collection when prop != null && xprop.Elements.Value.Count( ) > 0
                           && prop.PropertyType.GetInterface( "IEnumerable" ) != null
                           && ParsePropertyCollection( xprop.Elements.Value, prop, page, xprop, parseInfo, out res ):
                           //property is collection
                           break;

                       case var setter when prop != null && xprop.Elements.Value.Count( ) == 1
                               && ParsePropertySet( xprop, prop, page, parseInfo, out res ):
                           //element has property with setter where to install parsed element
                           break;

                       case var bindable when prop != null && xprop.IsBindable
                               && ParseBindable( xprop, page, parseInfo, prop, out res ):
                           //bindable field
                           break;

                       case var resource when prop != null && xprop.IsStaticResource
                               && ParseStaticResource( resource, prop, out res ):
                           //static resource like style or static object
                           break;

                       case var dynResource when prop != null && xprop.IsDynamicResource
                               && ParseDynamicResource( dynResource, prop, out res ):
                           //dynamic resource like style or static object
                           break;

                       case var xpath when prop != null && xprop.IsXPath
                               && ParseXPath( xpath, page, prop, out res ):
                           //resolve xpath to get value
                           break;

                       case var value when prop != null
                               && ParsePropertyValue( value, prop, parseInfo, out res ):
                           //default case, parse value and set to the property
                           break;

                       default:
                           Log.Warning( "PARSE PROPERTY {name} not recognized", xprop.Name );
                           break;
                   }

                   return res;
               } );

            foreach( var res in propRes )
            {
                var retValLoc = retVal;

                retVal = async ( elem ) =>
                {
                    var res1 = await retValLoc( elem );
                    var (on, off) = await res( elem );

                    return (JoinAction( res1.on, on ), JoinAction( res1.off, off ));
                };
            }

            return retVal;
        }

        private XPathResolver NewXPathResolver( XElement x, PageObject page )
        {
            return new XPathResolver( x )
            {
                TransformPathAndBase = ( path ) => (null, path.StartsWith( FormDataScript )
                    ? path.Replace( FormDataScript, page.XPathData )
                    : path)
            };
        }
    }

    public static class ParseExtension
    {
        public static ParseInfo Merge( this ParseInfo p1, ParseInfo p2 )
        {
            if( p1 != null && p2 != null )
            {
                p2.IsTemplate = p1.IsTemplate;
                p2.TemplateBindableInfo = p1.TemplateBindableInfo;
                p2.LastPropertyIndex = p1.LastPropertyIndex;

                return p2;
            }
            else if( p1 != null && p2 == null )
            {
                return p1;
            }
            else
            {
                return p2;
            }
        }
    }
}
