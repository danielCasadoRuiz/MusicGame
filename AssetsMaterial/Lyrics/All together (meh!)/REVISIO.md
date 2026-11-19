# All together (meh!) — prova de transcripció

Àudio: The Backup Boys — All together (meh!).wav, 221,96 segons.
Motor: Whisper medium.en mitjançant faster-whisper, CPU int8.
Processament local. No s'ha enviat l'àudio a cap servidor.
La lletra facilitada per l'autor no s'ha introduït com a prompt del reconeixement.

## Fitxers

- `lyrics.suno-reference.txt`: referència provisional facilitada per l'autor, amb grafia fonètica i vocals allargades. No es confirma que sigui la versió final.
- `lyrics.display-draft.txt`: proposta de text net basada en aquesta referència. «Meaah» es mostra com «meh» per indicació de l'autor. No modifica la composició ni confirma què canta l'enregistrament.
- `whisper.raw.txt`: transcripció automàtica sense correccions editorials.
- `whisper.raw.json`: transcripció amb temps per segment i paraula i estimacions de confiança del model.
- `whisper.draft.srt` i `whisper.draft.lrc`: exportacions de la transcripció automàtica per revisar-la amb l'àudio. No són lletres finals del joc.

## Discrepàncies que cal comprovar

| Temps aproximat | Referència de l'autor | Lectura de Whisper |
| --- | --- | --- |
| 00:45 | Bringing something fine somehow | Bring in something, find some hope |
| 00:58 | Do I drive you wild? | Do I drive your wheel? |
| 01:00 | Am I all you're dreaming of? | Am I all your, all your dreamin' of it? |
| 01:06 i tornades | yah — meh | you're me / yeah, me, yeah |
| 01:40 | side to side | side to side to side |
| 01:46 | then make some noise | then, then make some noise |
| 01:57 | Am I your chosen guy? | Am I your chosen kind? |
| 01:59 | Do I drive you wild? | Do I drive you weird? |
| 03:16–03:34 | veus principals i respostes superposades de la tornada final | fragments parcials i possibles errors, com «We turn motion right» i «Feeling down» |

Aquesta taula compara dos textos; no afirma que una lectura sigui la que realment sona.
Whisper també pot ometre interjeccions i cors. Les probabilitats del model no garanteixen que una paraula sigui correcta, encara que siguin altes.

## Criteri per preparar el karaoke

Mostrar paraules amb ortografia normal: «together», «feeling», «meh».
Representar les notes llargues amb duració i ressaltat, sense repetir vocals.
Conservar la referència de Suno separada del text de lectura.
Els temps de Whisper són una primera estimació: cal revisar-los i ajustar-los amb el text corregit, especialment en vocals llargues, instruments i cors.
No s'han creat temps ficticis per als «meh» que el model no ha separat.
No s'ha incorporat cap d'aquests esborranys al Jukebox.
