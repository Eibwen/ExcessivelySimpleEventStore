# ExcessivelySimpleEventStore

[![NuGet version (ExcessivelySimpleEventStore)](https://img.shields.io/nuget/v/ExcessivelySimpleEventStore.svg?style=flat-square)](https://www.nuget.org/packages/ExcessivelySimpleEventStore/)
[![Build Status](https://travis-ci.com/Eibwen/ExcessivelySimpleEventStore.svg?branch=master)](https://travis-ci.com/Eibwen/ExcessivelySimpleEventStore)

## Design
This first version was an attempt at making an EventStore library without ever working directly with one before.  I felt I understood the concepts well enough, but didn't know the concrete designs/interfaces out there.  So wanted to see how good of interface I could make in C# that would work how I'd want it to.

At times during this process I've messed up some concepts, and that could still be the case.  Namely it is currently both a `EventStreamStore` and a `CurrentStateStore`... Need to decide if thats what I intend or not

The next step is to compare to other libraries/interfaces and see if they have accomplished anything more elegantly than I.

This is **not** currently optimized for speed, nor reliability, nor anything really.  It is designed to be a very simple interface and usage that requires you to follow Event Sourcing principles.  And allows you to do so without any extra overhead at all.

## Usage
To use this, you must first implement a `Controller`, which is just a class which has various public methods matching `public void {commandName}(IEventStoreAction<{yourDataType}> datastore, {yourCommandData} cmd)`

`yourDataType` can be a full object tree that you'd need static storage of, your command methods are able to interact with that object, allowing you to debug those interactions

then you're able to create/load your datastore:
```
var datastore = new EventStore<MyController, MyDataType>(controllerInstance, x => x.Id.ToString(), fileSystem, dataStoreFilePath);
```

### TODO list
- [ ] Make a nuget
- [ ] Compare with other frameworks, support/convert to their interface?
- [ ] (Code) I could have a `Func<TValue, TId> idSelector` then a `Func<TId, string> idNormalizer`, which would allow me to make `Get` nicer to use
- [ ] (Code) wait why did I decide this should all be strings??  Just so serialization is easier?
- [ ] (Code) Add an overload of `ExecuteEvent` which is `Expression<TController>` (also with `ExecuteEvent<TControllerMethod, TEvent>)
- [ ] 