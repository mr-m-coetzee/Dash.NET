﻿namespace Dash.NET

open Giraffe
open GiraffeViewEngine
open Views
open Plotly.NET
open System 
open System.Collections.Generic
open System.Runtime.InteropServices

type DashApp =
    {
        Index: IndexView
        Layout: obj //This will have a proper type when the DSL for components is in place
        Config: DashConfig
        Callbacks: CallbackMap
    }

    static member initDefault() =
        {
            Index = IndexView.initDefault()
            Layout = obj
            Config = DashConfig.initDefault()
            Callbacks = CallbackMap()
        }

    static member initDefaultWith (initializer: DashApp -> DashApp) = DashApp.initDefault () |> initializer

    static member withConfig (config: DashConfig) (app: DashApp) = 
        { app with 
            Config = config 
            Index = app.Index |> IndexView.withConfig config
        }

    static member withIndex (index:IndexView) (app:DashApp) =
        { app with 
            Index = index |> IndexView.withConfig app.Config
        }

    static member mapIndex (f: IndexView -> IndexView) (app:DashApp) =
        { app with 
            Index = f app.Index 
        }

    static member addCSSLinks (hrefs:seq<string>) (app:DashApp) =
        let tags = 
            hrefs
            |> Seq.map (fun href ->
                link [_rel "stylesheet"; _href href ; _crossorigin " "]
            )
        app
        |> DashApp.mapIndex (IndexView.addCSSLinks tags)

    static member addScripts (sources:seq<string>) (app:DashApp) =
        let tags = 
            sources
            |> Seq.map (fun source ->
                script [_type "application/javascript"; _crossorigin " "; _src source] []
            )
        app
        |> DashApp.mapIndex (IndexView.addScripts tags)

    static member withLayout (layout:obj) (app:DashApp) =
        { app with 
            Layout = layout
        }

    static member withCallbackHandler (callback: Callback<'Function>) (app: DashApp) =


        //To-Do: Maybe use copy utility for all direct calls to underlying DynamicObjs.
        { app with
            Callbacks =
                app.Callbacks
                |> CallbackMap.registerCallback (callback.Output |> Dependency.toCompositeId) callback
        }

    static member withCallbackHandlers (callbacks: seq<(string * Callback<'Function>)>) (app: DashApp) =
        //To-Do: Maybe use copy utility for all direct calls to underlying DynamicObjs.
        { app with
            Callbacks =
                callbacks
                |> Seq.fold (fun cMap (cId, cHandler) ->
                    cMap
                    |> CallbackMap.registerCallback cId cHandler) app.Callbacks
        }

    static member getIndexHTML (app:DashApp) =
        app.Index |> IndexView.toHTMLComponent

    static member toWebApp (app:DashApp) =
        choose [
            GET >=>
                choose [
                    //serve the index
                    route "/" >=> htmlView (app |> DashApp.getIndexHTML)

                    //Dash GET enpoints
                    route "/_dash-layout"       >=> json app.Layout        //Calls from Dash renderer for what components to render (must return serialized dash components)
                    route "/_dash-dependencies" >=> json (app.Callbacks |> CallbackMap.toDependencies) //Serves callback bindings as json on app start.
                    route "/_reload-hash"       >=> json obj               //This call is done when using hot reload.
                ]

            POST >=> 
                choose [
                    //Dash POST endpoints
                    route "/_dash-update-component" //calls from callbacks come in here.
                        >=> bindJson ( fun (cbRequest:CallbackRequest) -> 

                            let inputs = 
                                let inputs = cbRequest.Inputs |> Array.map (fun reqInput -> box reqInput.Value) //generate argument list for the callback
                                let states = 
                                    //Yes, this is ugly. I currently cant find a way to deserialize an empty array directly when the state property is missing in the JSON, but that sounds like a problem that is solvable.
                                    try
                                        cbRequest.State |> Array.map (fun reqInput -> box reqInput.Value)
                                    with _ ->
                                        [||]
                                Array.append inputs states

                            let result = 
                                app.Callbacks
                                |> CallbackMap.getPackedCallbackById (cbRequest.Output) //get the callback from then callback map
                                |> Callback.getResponseObject inputs //evaluate the handler function and get the response to send to the client

                            json result //return serialized result of the handler function
                        )
                    
                ]
            setStatusCode 404 >=> text "Not Found" 
        ]
