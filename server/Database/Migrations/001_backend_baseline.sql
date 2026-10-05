CREATE TABLE IF NOT EXISTS players (
  player_id BIGINT NOT NULL AUTO_INCREMENT,
  external_provider VARCHAR(32) NOT NULL,
  external_subject VARCHAR(191) NOT NULL,
  display_name VARCHAR(96) NOT NULL,
  home_realm_id VARCHAR(64) NOT NULL,
  revision BIGINT NOT NULL DEFAULT 0,
  created_at DATETIME(6) NOT NULL,
  PRIMARY KEY (player_id),
  UNIQUE KEY uq_players_identity (external_provider, external_subject)
)
ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS sessions (
  token_hash CHAR(64) NOT NULL,
  player_id BIGINT NOT NULL,
  home_realm_id VARCHAR(64) NOT NULL,
  expires_at DATETIME(6) NOT NULL,
  created_at DATETIME(6) NOT NULL,
  revoked_at DATETIME(6) NULL,
  PRIMARY KEY (token_hash),
  KEY ix_sessions_player (player_id, home_realm_id),
  CONSTRAINT fk_sessions_player FOREIGN KEY (player_id) REFERENCES players(player_id)
)
ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS wallets (
  player_id BIGINT NOT NULL,
  home_realm_id VARCHAR(64) NOT NULL,
  yuan_shi BIGINT NOT NULL DEFAULT 0,
  xian_yuan_shi BIGINT NOT NULL DEFAULT 0,
  revision BIGINT NOT NULL DEFAULT 0,
  PRIMARY KEY (player_id, home_realm_id),
  CONSTRAINT fk_wallets_player FOREIGN KEY (player_id) REFERENCES players(player_id)
)
ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS inventory_items (
  player_id BIGINT NOT NULL,
  home_realm_id VARCHAR(64) NOT NULL,
  item_id VARCHAR(96) NOT NULL,
  quantity BIGINT NOT NULL DEFAULT 0,
  revision BIGINT NOT NULL DEFAULT 0,
  PRIMARY KEY (player_id, home_realm_id, item_id),
  CONSTRAINT fk_inventory_player FOREIGN KEY (player_id) REFERENCES players(player_id)
)
ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS quest_states (
  player_id BIGINT NOT NULL,
  home_realm_id VARCHAR(64) NOT NULL,
  quest_id VARCHAR(96) NOT NULL,
  state VARCHAR(32) NOT NULL DEFAULT 'NotStarted',
  progress_json JSON NOT NULL,
  revision BIGINT NOT NULL DEFAULT 0,
  PRIMARY KEY (player_id, home_realm_id, quest_id),
  CONSTRAINT fk_quest_player FOREIGN KEY (player_id) REFERENCES players(player_id)
)
ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS activity_claims (
  player_id BIGINT NOT NULL,
  home_realm_id VARCHAR(64) NOT NULL,
  activity_id VARCHAR(96) NOT NULL,
  reward_id VARCHAR(96) NOT NULL,
  operation_id VARCHAR(96) NOT NULL,
  claimed_at DATETIME(6) NOT NULL,
  PRIMARY KEY (player_id, home_realm_id, activity_id, reward_id),
  UNIQUE KEY uq_activity_claim_operation (player_id, operation_id),
  CONSTRAINT fk_activity_player FOREIGN KEY (player_id) REFERENCES players(player_id)
)
ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS world_instances (
  world_cluster_id VARCHAR(64) NOT NULL,
  instance_id VARCHAR(96) NOT NULL,
  scene_id VARCHAR(96) NOT NULL,
  endpoint VARCHAR(255) NOT NULL,
  capacity INT NOT NULL,
  epoch BIGINT NOT NULL,
  heartbeat_at DATETIME(6) NOT NULL,
  PRIMARY KEY (world_cluster_id, instance_id)
)
ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS world_routes (
  player_id BIGINT NOT NULL,
  home_realm_id VARCHAR(64) NOT NULL,
  world_cluster_id VARCHAR(64) NOT NULL,
  instance_id VARCHAR(96) NOT NULL,
  scene_id VARCHAR(96) NOT NULL,
  endpoint VARCHAR(255) NOT NULL,
  epoch BIGINT NOT NULL,
  updated_at DATETIME(6) NOT NULL,
  PRIMARY KEY (player_id, home_realm_id),
  CONSTRAINT fk_routes_player FOREIGN KEY (player_id) REFERENCES players(player_id)
)
ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS battle_rooms (
  room_id CHAR(32) NOT NULL,
  player_id BIGINT NOT NULL,
  home_realm_id VARCHAR(64) NOT NULL,
  world_cluster_id VARCHAR(64) NOT NULL,
  instance_id VARCHAR(96) NOT NULL,
  scene_id VARCHAR(96) NOT NULL,
  world_epoch BIGINT NOT NULL,
  simulation_version VARCHAR(64) NOT NULL,
  config_version VARCHAR(64) NOT NULL,
  state VARCHAR(32) NOT NULL,
  created_at DATETIME(6) NOT NULL,
  ended_at DATETIME(6) NULL,
  PRIMARY KEY (room_id),
  KEY ix_battle_player (player_id, state),
  CONSTRAINT fk_battle_player FOREIGN KEY (player_id) REFERENCES players(player_id)
)
ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS domain_command_inbox (
  domain_name VARCHAR(32) NOT NULL,
  player_id BIGINT NOT NULL,
  operation_id VARCHAR(96) NOT NULL,
  request_hash CHAR(64) NOT NULL,
  payload_json JSON NOT NULL,
  status VARCHAR(32) NOT NULL,
  accepted_at DATETIME(6) NOT NULL,
  PRIMARY KEY (domain_name, player_id, operation_id),
  CONSTRAINT fk_command_player FOREIGN KEY (player_id) REFERENCES players(player_id)
)
ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS service_outbox (
  event_id BIGINT NOT NULL AUTO_INCREMENT,
  topic VARCHAR(96) NOT NULL,
  aggregate_id VARCHAR(96) NOT NULL,
  payload_json JSON NOT NULL,
  created_at DATETIME(6) NOT NULL,
  published_at DATETIME(6) NULL,
  PRIMARY KEY (event_id),
  KEY ix_outbox_pending (published_at, event_id)
)
ENGINE=InnoDB;
