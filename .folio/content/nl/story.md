# Verhaal

Een portfolio veroudert zodra het beschrijven van het werk een losse taak wordt. De projecten blijven
veranderen; de alinea erover staat ergens waar die verandering nooit langskomt.

Die beschrijving verhuist daarom naar de repository waar hij over gaat, in dezelfde commit als de
verandering. Een project documenteert zichzelf, naast zijn eigen code, en de site leest wat die
repository vandaag zegt.

Het formaat staat op twee plekken en die overlappen niet. Centraal staat wat over de site gaat: welke
talen er zijn, welke projecten meedoen en in welke volgorde. Per repository staat wat over dat ene
project gaat. Geen van beide overschrijft de ander, want ze zeggen nooit hetzelfde — en daarmee
begint geen enkele debugsessie meer met de vraag welk bestand gewonnen heeft.

Alles wordt periodiek opnieuw opgebouwd tot één onveranderlijke snapshot, en verzoeken lezen daaruit.
Geen enkel verzoek gaat naar GitHub, dus een trage upstream of een rate limit is niet langer het
probleem van de bezoeker. Mislukt een refresh, dan blijft de vorige snapshot gewoon staan.

De keerzijde, meteen maar gezegd: een project dat niets te vertellen heeft, laat ook niets zien. De
API verzint geen beschrijving, en een repository die er geen heeft gekregen verschijnt met alleen wat
GitHub meldt.
