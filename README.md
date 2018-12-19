# ExcessivelySimpleEventStore


## TODO list
- [ ] Make a nuget
- [ ] Compare with other frameworks, support/convert to their interface?
- [ ] (Code) I could have a `Func<TValue, TId> idSelector` then a `Func<TId, string> idNormalizer`, which would allow me to make `Get` nicer to use
- [ ] (Code) wait why did I decide this should all be strings??  Just so serialization is easier?
- [ ] (Code) Add an overload of `ExecuteEvent` which is `Expression<TController>` (also with `ExecuteEvent<TControllerMethod, TEvent>)
- [ ] 