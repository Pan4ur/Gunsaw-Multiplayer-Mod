An alternative way to replicate mechanisms based on the following principle: the host sends a signal telling the door where to move, and the clients move it themselves

Pros

Mechanisms move extremely smoothly on the clients
Less load on the host/network because explicit mechanism coordinates and velocities no longer need to be transmitted (only when a new player joins, to synchronize the scene for them)

Cons

When the mechanism is an elevator with containers on it, ping can cause the containers to fall through the elevator or simply break, because clients tick the mechanisms themselves while prop positions are still controlled by the host (as they should be)

I consider the experiment half-successful and will leave it here until I figure out a proper way to synchronize the props
