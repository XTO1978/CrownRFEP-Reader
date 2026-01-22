import express from 'express';
import db from '../db/database.js';
import { S3Client, ListObjectsV2Command } from '@aws-sdk/client-s3';

const router = express.Router();

let s3Client = null;

function getS3Client() {
  if (!s3Client) {
    s3Client = new S3Client({
      region: process.env.WASABI_REGION || 'eu-west-2',
      endpoint: process.env.WASABI_ENDPOINT || 'https://s3.eu-west-2.wasabisys.com',
      credentials: {
        accessKeyId: process.env.WASABI_ACCESS_KEY,
        secretAccessKey: process.env.WASABI_SECRET_KEY
      },
      forcePathStyle: true
    });
  }
  return s3Client;
}

function getBucket() {
  return process.env.WASABI_BUCKET || 'crownanalyzer';
}

function safeDbAll(sql, params = []) {
  try {
    return db.prepare(sql).all(...params);
  } catch (err) {
    return null;
  }
}

router.get('/users', (req, res) => {
  try {
    const rows = safeDbAll(
      'SELECT u.id, u.email, u.name, u.role, u.team_id, u.created_at, u.last_login, t.name as team_name, t.wasabi_folder FROM users u LEFT JOIN teams t ON u.team_id = t.id'
    );

    if (rows) {
      return res.json({ success: true, users: rows });
    }

    const users = db.prepare('SELECT * FROM users').all();
    const teams = db.prepare('SELECT * FROM teams').all();

    const mapped = users.map(u => {
      const team = teams.find(t => t.id === u.team_id);
      return {
        id: u.id,
        email: u.email,
        name: u.name,
        role: u.role,
        team_id: u.team_id,
        created_at: u.created_at,
        last_login: u.last_login,
        team_name: team?.name || null,
        wasabi_folder: team?.wasabi_folder || null
      };
    });

    res.json({ success: true, users: mapped });
  } catch (err) {
    console.error('[Admin] Error listando usuarios:', err);
    res.status(500).json({ error: 'Error listando usuarios' });
  }
});

router.get('/teams', (req, res) => {
  try {
    const teams = db.prepare('SELECT * FROM teams').all();
    res.json({ success: true, teams });
  } catch (err) {
    console.error('[Admin] Error listando equipos:', err);
    res.status(500).json({ error: 'Error listando equipos' });
  }
});

router.get('/metrics', (req, res) => {
  try {
    const users = db.prepare('SELECT * FROM users').all();
    const teams = db.prepare('SELECT * FROM teams').all();
    res.json({
      success: true,
      totals: {
        users: users.length,
        teams: teams.length
      }
    });
  } catch (err) {
    console.error('[Admin] Error métricas:', err);
    res.status(500).json({ error: 'Error métricas' });
  }
});

router.get('/wasabi/stats', async (req, res) => {
  try {
    const teams = db.prepare('SELECT * FROM teams').all();
    const client = getS3Client();

    const results = [];
    let totalBytes = 0;
    let totalObjects = 0;

    for (const team of teams) {
      const prefix = `${team.wasabi_folder}/`;
      let continuationToken = undefined;
      let teamBytes = 0;
      let teamObjects = 0;
      let lastModified = null;

      do {
        const command = new ListObjectsV2Command({
          Bucket: getBucket(),
          Prefix: prefix,
          ContinuationToken: continuationToken
        });

        const response = await client.send(command);
        const items = response.Contents || [];

        for (const item of items) {
          teamObjects += 1;
          teamBytes += item.Size || 0;
          if (!lastModified || (item.LastModified && item.LastModified > lastModified)) {
            lastModified = item.LastModified;
          }
        }

        continuationToken = response.IsTruncated ? response.NextContinuationToken : undefined;
      } while (continuationToken);

      totalBytes += teamBytes;
      totalObjects += teamObjects;

      results.push({
        teamId: team.id,
        teamName: team.name,
        wasabiFolder: team.wasabi_folder,
        objectCount: teamObjects,
        totalBytes: teamBytes,
        lastModified
      });
    }

    res.json({
      success: true,
      totals: { totalBytes, totalObjects },
      teams: results
    });
  } catch (err) {
    console.error('[Admin] Error Wasabi stats:', err);
    res.status(500).json({ error: 'Error stats Wasabi' });
  }
});

export default router;
