# Trading on Polymarket: what it takes

Orders are signed, not logged in. Polymarket's order API needs: a wallet holding USDC on Polygon (accounts made with an email login use a proxy wallet controlled by an exported key); API credentials derived once by signing an EIP-712 message; an HMAC signature on every request; and an EIP-712 signature on every order. The key would be stored with Windows DPAPI.

Before any of this is built the location question has to be settled: Polymarket's own check (`https://polymarket.com/api/geoblock`) reports the country, and the app will not send orders when it says `blocked`. Reading prices is not restricted.

Also open: positions, open orders and balance panels, green-up (sell what you hold or buy the opposite outcome), and automation rules.
