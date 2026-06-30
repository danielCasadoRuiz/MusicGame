using UnityEngine;

public struct GameStartedEvent   { }
public struct GameEndedEvent     { public CollectionStats Stats; }
public struct LevelGeneratedEvent { public int RingCount; }
public struct RingCollectedEvent  { public RingType Type; public Vector3 Position; }
