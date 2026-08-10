# Overzicht

Een read-only HTTP-API die een portfolio samenstelt uit twee bronnen: wat GitHub over een repository
weet, en wat die repository zelf vertelt in een `.folio`-map naast de code.

De structuur staat in TOML, de teksten in markdown, en alles wat een bezoeker te zien krijgt staat in
de map van de taal waar het bij hoort. Een taal erbij betekent bestanden erbij, nooit bestaande
bestanden aanpassen.

De API meldt wat hij vindt en waar het vandaan komt — ook wanneer een Nederlandse tekst terugvalt op
het Engels. Wat je daarmee laat zien, bepaalt de frontend.
