0. Download zip
0. Run ApplicationReviewer
0. AI verify proofs
0. Manually verify application 
0. If top 10 or person of interest post about it on Social Media
0. Reply to athlete

ApplicationReviewer builds/restores the website (including frontend assets) in its own Debug/Release configuration before starting a new preview. It validates the homepage and local CSS/JavaScript before extracting any application. An already-running server is reused only if those checks pass; if it serves stale placeholders or missing assets, stop that local website and rerun the reviewer. The reviewer does not terminate an existing server it did not start.
