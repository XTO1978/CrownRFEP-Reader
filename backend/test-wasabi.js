import { S3Client, PutObjectCommand, ListBucketsCommand, ListObjectsV2Command } from '@aws-sdk/client-s3';
import dotenv from 'dotenv';
dotenv.config();

const s3 = new S3Client({
  region: process.env.WASABI_REGION,
  endpoint: 'https://s3.eu-west-2.wasabisys.com',
  credentials: {
    accessKeyId: process.env.WASABI_ACCESS_KEY,
    secretAccessKey: process.env.WASABI_SECRET_KEY
  },
  forcePathStyle: true
});

async function test() {
  try {
    console.log('� Contando archivos en CrownRFEP/sessions/...');
    
    let total = 0;
    let totalSize = 0;
    let token = undefined;
    
    do {
      const response = await s3.send(new ListObjectsV2Command({
        Bucket: 'crownanalyzer',
        Prefix: 'CrownRFEP/sessions/',
        MaxKeys: 1000,
        ContinuationToken: token
      }));
      
      if (response.Contents) {
        total += response.Contents.length;
        totalSize += response.Contents.reduce((sum, item) => sum + (item.Size || 0), 0);
      }
      token = response.NextContinuationToken;
    } while (token);
    
    console.log('✅ Total archivos:', total);
    console.log('✅ Total tamaño:', (totalSize / 1024 / 1024 / 1024).toFixed(2), 'GB');
    
  } catch (err) {
    console.error('❌ Error:', err.message);
  }
}

test();
