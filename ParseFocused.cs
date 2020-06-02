using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Xamarin.Forms;

namespace Code.Service.Render
{
    public partial class RenderService
    {
        //Implemented only for non template xaml
        private bool ParseFocused( Type elementType, XNodeInfo xinfo, ParseInfo parseInfo,
            PageObject page, out Func<object, Task<(Action on, Action off)>> result )
        {
            if( xinfo.Name == "IsFocused" )
            {
                Log.Information( "PARSE PROPERTY 'IsFocused'" );

                BindableInfo bindableInfo;

                try
                {
                    bindableInfo = BindableInfo.Parse( xinfo, parseInfo );
                }
                catch( Exception ex )
                {
                    result = ( elem ) => DefaultTask2Action;

                    Log.Error( ex, "PARSE ERROR {name} property is not a bindable -> {xpath}",
                                xinfo.Name, xinfo.XNodePath );

                    return false;
                }

                result = ( elem ) =>
                {
                    IXPathResult bindingRes = null;
                    VisualElement element = null;
                    Action<string> updateXmlTree = null;

                    if( ( element = elem as VisualElement ) == null || bindableInfo == null )
                    {
                        return DefaultTask2Action;
                    }

                    Task resultChangedHandler( IXPathResult xres )
                    {
                        var val = xres.SingleResultValue( );

                        if( string.IsNullOrEmpty( val ) == false && bool.TryParse( val, out bool boolRes ) )
                        {
                            if( boolRes && element.IsFocused == false )
                            {
                                element.Focus( );
                            }
                            else if( boolRes == false && element.IsFocused )
                            {
                                element.Unfocus( );
                            }
                        }

                        return Task.CompletedTask;
                    }

                    void ElementFocused( object sender, FocusEventArgs e )
                    {
                        updateXmlTree?.Invoke( bool.TrueString );
                    }

                    void ElementUnfocused( object sender, FocusEventArgs e )
                    {
                        updateXmlTree?.Invoke( bool.FalseString );
                    }

                    void on( )
                    {
                        bindingRes = _treeService.ResolveXPath( bindableInfo.XPath, resolver: page.DataResolver );

                        updateXmlTree = GetUpdateXmlTreeFunc( bindableInfo, bindingRes );

                        element.Focused += ElementFocused;
                        element.Unfocused += ElementUnfocused;
                        bindingRes.ResultChanged += resultChangedHandler;
                    }

                    void off( )
                    {
                        if( bindingRes != null )
                            bindingRes.ResultChanged -= resultChangedHandler;

                        element.Focused -= ElementFocused;
                        element.Unfocused -= ElementUnfocused;

                        updateXmlTree = null;
                    }

                    return Task.FromResult<(Action, Action)>( (on, off) );
                };

                return true;
            }

            result = ( elem ) => DefaultTask2Action;
            return false;
        }
    }
}
