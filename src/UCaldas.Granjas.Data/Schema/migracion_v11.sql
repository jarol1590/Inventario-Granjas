-- ============================================================================
-- v2.11: formas de pago del Recibo de Caja SP. Se agregan TRANSFERENCIA y RECURSOS_PROPIOS; EFECTIVO_CHEQUE
-- se conserva solo para los recibos ya emitidos (ya no se puede elegir). Ninguna tabla tiene llave foránea
-- hacia RECIBO_CAJA, así que se reconstruye conservando los datos y los ids.
-- ============================================================================

CREATE TABLE RECIBO_CAJA_NUEVA (
    id                INTEGER PRIMARY KEY AUTOINCREMENT,
    numero_recibo     INTEGER NOT NULL UNIQUE,
    prefijo           TEXT NOT NULL DEFAULT 'SP',
    remision_id       INTEGER NOT NULL UNIQUE REFERENCES REMISION(id),
    fecha             DATETIME NOT NULL,
    forma_pago        TEXT NOT NULL CHECK (forma_pago IN ('CONSIGNACION', 'TRANSFERENCIA', 'RECURSOS_PROPIOS', 'EFECTIVO_CHEQUE', 'TRASLADO_INTERNO')),
    banco_consignacion TEXT,
    es_efectivo       BOOLEAN NOT NULL DEFAULT 0,
    cheque1_num      TEXT,
    cheque2_num      TEXT,
    total_pagado      DECIMAL(12,2) NOT NULL,
    total_letras      TEXT NOT NULL,
    observaciones     TEXT,
    anulado           BOOLEAN NOT NULL DEFAULT 0
);

INSERT INTO RECIBO_CAJA_NUEVA (id, numero_recibo, prefijo, remision_id, fecha, forma_pago, banco_consignacion, es_efectivo,
                               cheque1_num, cheque2_num, total_pagado, total_letras, observaciones, anulado)
SELECT id, numero_recibo, prefijo, remision_id, fecha, forma_pago, banco_consignacion, es_efectivo,
       cheque1_num, cheque2_num, total_pagado, total_letras, observaciones, anulado FROM RECIBO_CAJA;

DROP TABLE RECIBO_CAJA;
ALTER TABLE RECIBO_CAJA_NUEVA RENAME TO RECIBO_CAJA;
